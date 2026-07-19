using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StockWebApplications.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StockWebApplications
{
    // Angel One SmartAPI market-data client.
    // Credentials in appsettings.json -> AngelOne (ApiKey, ClientCode, Pin, TotpSecret).
    // When not configured, callers fall back to the NSE source.
    public class AngelOneClient
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        private const string LoginUrl = "https://apiconnect.angelone.in/rest/auth/angelbroking/user/v1/loginByPassword";
        private const string QuoteUrl = "https://apiconnect.angelone.in/rest/secure/angelbroking/market/v1/quote/";
        private const string ScripMasterUrl = "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";

        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public AngelOneClient(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            _config = config;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_config["AngelOne:ApiKey"]) &&
            !string.IsNullOrWhiteSpace(_config["AngelOne:ClientCode"]) &&
            !string.IsNullOrWhiteSpace(_config["AngelOne:Pin"]) &&
            !string.IsNullOrWhiteSpace(_config["AngelOne:TotpSecret"]);

        //-------------------------------------------------------------
        // LTP per strategy leg: resolve NFO instrument tokens from the
        // scrip master, then fetch all LTPs in one quote call.
        //-------------------------------------------------------------
        public async Task<Dictionary<int, decimal?>> GetLegLtpsAsync(string symbol, List<StrategyLegVM> legs, DateTime fallbackExpiry)
        {
            var name = (symbol ?? "").Trim().ToUpperInvariant();
            var map = await GetInstrumentMapAsync();

            var legTokens = new Dictionary<int, string>();

            foreach (var leg in legs)
            {
                var expiry = (leg.ExpiryDate ?? fallbackExpiry).Date;
                if (expiry.Year < 2000) continue;

                string type;
                decimal strike = 0m;

                if (string.Equals(leg.InstrumentType, "FUTURE", StringComparison.OrdinalIgnoreCase))
                {
                    type = "FUT";
                }
                else if (string.Equals(leg.InstrumentType, "CE", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(leg.InstrumentType, "PE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!leg.StrikePrice.HasValue) continue;
                    type = leg.InstrumentType.ToUpperInvariant();
                    strike = leg.StrikePrice.Value;
                }
                else
                {
                    continue;
                }

                if (map.TryGetValue((name, expiry, strike, type), out var token))
                {
                    legTokens[leg.LegId] = token;
                }
            }

            var ltpByToken = await GetLtpsAsync(legTokens.Values.Distinct().ToList());

            var result = new Dictionary<int, decimal?>();

            foreach (var leg in legs)
            {
                decimal? ltp = null;

                if (legTokens.TryGetValue(leg.LegId, out var token)
                    && ltpByToken.TryGetValue(token, out var v))
                {
                    ltp = v;
                }

                result[leg.LegId] = ltp;
            }

            return result;
        }

        //-------------------------------------------------------------
        // Quote API (mode LTP) with an 8s per-token cache
        //-------------------------------------------------------------
        private async Task<Dictionary<string, decimal>> GetLtpsAsync(List<string> tokens)
        {
            var result = new Dictionary<string, decimal>();
            var missing = new List<string>();

            foreach (var t in tokens)
            {
                if (_cache.TryGetValue("ao_ltp_" + t, out decimal v)) result[t] = v;
                else missing.Add(t);
            }

            if (missing.Count == 0) return result;

            var jwt = await GetJwtAsync();

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var payload = new JObject
                {
                    ["mode"] = "LTP",
                    ["exchangeTokens"] = new JObject { ["NFO"] = new JArray(missing) }
                };

                using var req = BuildRequest(HttpMethod.Post, QuoteUrl, jwt, payload);
                var resp = await http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();

                if ((int)resp.StatusCode == 401)
                {
                    jwt = await GetJwtAsync(force: true);
                    continue;
                }

                var json = JObject.Parse(text);

                if (json["status"]?.Value<bool>() != true)
                {
                    // AG8001/AG8002 = invalid/expired token — re-login once
                    var code = json["errorcode"]?.ToString();
                    if (attempt == 0 && (code == "AG8001" || code == "AG8002"))
                    {
                        jwt = await GetJwtAsync(force: true);
                        continue;
                    }
                    throw new Exception("Angel One quote failed: " + (json["message"]?.ToString() ?? text));
                }

                var fetched = json["data"]?["fetched"] as JArray;
                if (fetched != null)
                {
                    foreach (var item in fetched)
                    {
                        var token = item["symbolToken"]?.ToString();
                        var ltp = item["ltp"]?.Value<decimal?>();

                        if (!string.IsNullOrEmpty(token) && ltp.HasValue)
                        {
                            result[token] = ltp.Value;
                            _cache.Set("ao_ltp_" + token, ltp.Value, TimeSpan.FromSeconds(8));
                        }
                    }
                }

                break;
            }

            return result;
        }

        //-------------------------------------------------------------
        // NIFTY 50 1-minute candles with EMA10 / EMA30 crossover trend
        // Uses SmartAPI historical data endpoint.
        //-------------------------------------------------------------
        private const string CandleUrl = "https://apiconnect.angelone.in/rest/secure/angelbroking/historical/v1/getCandleData";

        // NIFTY 50 index token on NSE — well-known static value in the AngelOne universe.
        private const string NiftyIndexToken = "99926000";

        public class NiftyEmaRow
        {
            public string Candle { get; set; }
            public decimal Close { get; set; }
            public decimal Ema10 { get; set; }
            public decimal Ema30 { get; set; }
            public string Trend { get; set; }   // "SELL PE(Bullish)" (EMA10 > EMA30) / "SELL CE(Bearish)" (EMA10 < EMA30) / "—"
            public bool Cross { get; set; }     // true only on the exact bar where the bias flipped vs the previous bar
        }

        public async Task<List<NiftyEmaRow>> GetNiftyEmaTrendAsync(int rows = 15)
        {
            var jwt = await GetJwtAsync();

            // Pull today's trading window 09:15 → 15:30, but reach back a few extra days
            // so the EMAs are properly seeded before market open.
            var todayOpen  = DateTime.Today.AddHours(9).AddMinutes(15);
            var todayClose = DateTime.Today.AddHours(15).AddMinutes(30);
            var to   = DateTime.Now < todayClose ? DateTime.Now : todayClose;
            var from = todayOpen.AddDays(-5);         // covers weekends/holidays; API trims to trading minutes

            var payload = new JObject
            {
                ["exchange"]    = "NSE",
                ["symboltoken"] = NiftyIndexToken,
                ["interval"]    = "ONE_MINUTE",
                ["fromdate"]    = from.ToString("yyyy-MM-dd HH:mm"),
                ["todate"]      = to.ToString("yyyy-MM-dd HH:mm")
            };

            JArray data = null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var req = BuildRequest(HttpMethod.Post, CandleUrl, jwt, payload);
                var resp = await http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();

                if ((int)resp.StatusCode == 401)
                {
                    jwt = await GetJwtAsync(force: true);
                    continue;
                }

                var json = JObject.Parse(text);

                if (json["status"]?.Value<bool>() != true)
                {
                    var code = json["errorcode"]?.ToString();
                    if (attempt == 0 && (code == "AG8001" || code == "AG8002"))
                    {
                        jwt = await GetJwtAsync(force: true);
                        continue;
                    }
                    throw new Exception("Angel One candle fetch failed: " + (json["message"]?.ToString() ?? text));
                }

                data = json["data"] as JArray;
                break;
            }

            var result = new List<NiftyEmaRow>();
            if (data == null || data.Count == 0) return result;

            // Each element: [timestamp, open, high, low, close, volume]
            var closes = new List<decimal>(data.Count);
            var times  = new List<string>(data.Count);
            var stamps = new List<DateTime>(data.Count);

            foreach (var c in data)
            {
                if (c is not JArray a || a.Count < 5) continue;
                if (!decimal.TryParse(a[4]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var cl))
                    continue;

                var ts = a[0]?.ToString() ?? "";
                DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
                times.Add(dt == default ? ts : dt.ToString("HH:mm"));
                stamps.Add(dt);
                closes.Add(cl);
            }

            if (closes.Count == 0) return result;

            var ema10 = Ema(closes, 10);
            var ema30 = Ema(closes, 30);

            // Only render today's 09:15 → 15:30 window, but keep the previous
            // candle's EMA lookup so the FIRST visible row can still show a cross.
            var day  = DateTime.Today;
            var open = day.AddHours(9).AddMinutes(15);
            var close = day.AddHours(15).AddMinutes(30);

            for (int i = 0; i < closes.Count; i++)
            {
                if (stamps[i] == default) continue;
                if (stamps[i] < open || stamps[i] > close) continue;

                // Trend for THIS candle — bullish while EMA10 > EMA30, bearish while below.
                string trend =
                    ema10[i] > ema30[i] ? "SELL PE(Bullish)" :
                    ema10[i] < ema30[i] ? "SELL CE(Bearish)" : "—";

                // A "cross" is the exact bar where the trend flipped vs the previous bar.
                bool cross = false;
                if (i > 0)
                {
                    string prev =
                        ema10[i - 1] > ema30[i - 1] ? "SELL PE(Bullish)" :
                        ema10[i - 1] < ema30[i - 1] ? "SELL CE(Bearish)" : "—";
                    cross = prev != trend && trend != "—";
                }

                result.Add(new NiftyEmaRow
                {
                    Candle = times[i],
                    Close  = Math.Round(closes[i], 2),
                    Ema10  = Math.Round(ema10[i], 2),
                    Ema30  = Math.Round(ema30[i], 2),
                    Trend  = trend,
                    Cross  = cross
                });
            }

            // `rows` kept in the signature for compatibility; the whole trading day is returned.
            _ = rows;
            return result;
        }

        private static decimal[] Ema(List<decimal> values, int period)
        {
            var ema = new decimal[values.Count];
            if (values.Count == 0) return ema;

            decimal k = 2m / (period + 1m);
            ema[0] = values[0];

            for (int i = 1; i < values.Count; i++)
                ema[i] = values[i] * k + ema[i - 1] * (1m - k);

            return ema;
        }

        //-------------------------------------------------------------
        // Session (JWT) — login with PIN + generated TOTP, cached 6h
        //-------------------------------------------------------------
        private async Task<string> GetJwtAsync(bool force = false)
        {
            if (!force && _cache.TryGetValue("ao_jwt", out string cached)) return cached;

            // Sync clock offset against an external time source so a drifted
            // host clock does not break TOTP (30-second window).
            await EnsureClockOffsetAsync();

            var secret = _config["AngelOne:TotpSecret"];
            var utcNow = DateTime.UtcNow.Add(_clockOffset);

            // Try current window, then ±1 window, to survive borderline drift.
            var offsets = new[] { 0L, -1L, 1L };
            string lastMessage = null;

            foreach (var step in offsets)
            {
                var body = new JObject
                {
                    ["clientcode"] = _config["AngelOne:ClientCode"],
                    ["password"] = _config["AngelOne:Pin"],
                    ["totp"] = GenerateTotpAt(secret, utcNow.AddSeconds(step * 30)),
                    ["state"] = "stockwebapp"
                };

                using var req = BuildRequest(HttpMethod.Post, LoginUrl, null, body);
                var resp = await http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();

                var json = JObject.Parse(text);

                if (json["status"]?.Value<bool>() == true)
                {
                    var jwt = json["data"]?["jwtToken"]?.ToString();
                    if (string.IsNullOrWhiteSpace(jwt))
                        throw new Exception("Angel One login returned no jwtToken");

                    _cache.Set("ao_jwt", jwt, TimeSpan.FromHours(6));
                    return jwt;
                }

                lastMessage = json["message"]?.ToString() ?? text;
            }

            var diag = $" [serverUtc={DateTime.UtcNow:HH:mm:ss} correctedUtc={utcNow:HH:mm:ss} offsetSec={(int)_clockOffset.TotalSeconds} client={_config["AngelOne:ClientCode"]}]";
            throw new Exception("Angel One login failed: " + lastMessage + diag);
        }

        // Cached difference between real UTC (from an external HTTP Date header)
        // and this host's clock. Refreshed hourly. Prevents TOTP failures on
        // shared hosts whose system clock drifts beyond the 30-second window.
        private static TimeSpan _clockOffset = TimeSpan.Zero;
        private static DateTime _clockOffsetSyncedAtUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _clockLock = new SemaphoreSlim(1, 1);

        private static async Task EnsureClockOffsetAsync()
        {
            if ((DateTime.UtcNow - _clockOffsetSyncedAtUtc) < TimeSpan.FromHours(1))
                return;

            await _clockLock.WaitAsync();
            try
            {
                if ((DateTime.UtcNow - _clockOffsetSyncedAtUtc) < TimeSpan.FromHours(1))
                    return;

                foreach (var url in new[] { "https://www.google.com/generate_204", "https://www.cloudflare.com/" })
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Head, url);
                        using var resp = await http.SendAsync(req);
                        if (resp.Headers.Date.HasValue)
                        {
                            _clockOffset = resp.Headers.Date.Value.UtcDateTime - DateTime.UtcNow;
                            _clockOffsetSyncedAtUtc = DateTime.UtcNow;
                            return;
                        }
                    }
                    catch { }
                }

                // No external source reachable: fall back to local clock, but
                // remember we tried so we don't hammer the network every call.
                _clockOffsetSyncedAtUtc = DateTime.UtcNow;
            }
            finally
            {
                _clockLock.Release();
            }
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, string jwt, JObject body)
        {
            var req = new HttpRequestMessage(method, url);

            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("X-UserType", "USER");
            req.Headers.TryAddWithoutValidation("X-SourceID", "WEB");
            req.Headers.TryAddWithoutValidation("X-ClientLocalIP", "127.0.0.1");
            req.Headers.TryAddWithoutValidation("X-ClientPublicIP", "127.0.0.1");
            req.Headers.TryAddWithoutValidation("X-MACAddress", "00:00:00:00:00:00");
            req.Headers.TryAddWithoutValidation("X-PrivateKey", _config["AngelOne:ApiKey"]);

            if (!string.IsNullOrEmpty(jwt))
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + jwt);

            if (body != null)
                req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            return req;
        }

        //-------------------------------------------------------------
        // Instrument master: (name, expiry, strike, CE/PE/FUT) -> token.
        // ~40MB download, stream-parsed, cached 24h.
        //-------------------------------------------------------------
        private async Task<Dictionary<(string Name, DateTime Expiry, decimal Strike, string Type), string>> GetInstrumentMapAsync()
        {
            if (_cache.TryGetValue("ao_scripmap",
                out Dictionary<(string, DateTime, decimal, string), string> cached))
                return cached;

            var map = new Dictionary<(string, DateTime, decimal, string), string>();

            using var resp = await http.GetAsync(ScripMasterUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var sr = new StreamReader(stream);
            using var jr = new JsonTextReader(sr);

            while (await jr.ReadAsync())
            {
                if (jr.TokenType != JsonToken.StartObject) continue;

                var obj = await JObject.LoadAsync(jr);

                if (!string.Equals(obj["exch_seg"]?.ToString(), "NFO", StringComparison.OrdinalIgnoreCase))
                    continue;

                var itype = obj["instrumenttype"]?.ToString() ?? "";
                bool isOpt = itype == "OPTIDX" || itype == "OPTSTK";
                bool isFut = itype == "FUTIDX" || itype == "FUTSTK";
                if (!isOpt && !isFut) continue;

                var name = obj["name"]?.ToString()?.ToUpperInvariant();
                var token = obj["token"]?.ToString();
                var expiryStr = obj["expiry"]?.ToString();

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expiryStr))
                    continue;

                if (!TryParseExpiry(expiryStr, out var expiry)) continue;

                if (isFut)
                {
                    map[(name, expiry.Date, 0m, "FUT")] = token;
                }
                else
                {
                    var tradingSymbol = obj["symbol"]?.ToString() ?? "";

                    string type = tradingSymbol.EndsWith("CE", StringComparison.OrdinalIgnoreCase) ? "CE"
                                : tradingSymbol.EndsWith("PE", StringComparison.OrdinalIgnoreCase) ? "PE"
                                : null;
                    if (type == null) continue;

                    // strike comes in paise
                    if (!decimal.TryParse(obj["strike"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var strikeRaw))
                        continue;

                    map[(name, expiry.Date, strikeRaw / 100m, type)] = token;
                }
            }

            _cache.Set("ao_scripmap", map, TimeSpan.FromHours(24));

            return map;
        }

        private static readonly string[] Months =
            { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

        // Scrip master expiry format: 28JUL2026
        private static bool TryParseExpiry(string s, out DateTime d)
        {
            d = default;
            s = s.Trim().ToUpperInvariant();
            if (s.Length != 9) return false;

            if (!int.TryParse(s.Substring(0, 2), out var day)) return false;

            var month = Array.IndexOf(Months, s.Substring(2, 3)) + 1;
            if (month == 0) return false;

            if (!int.TryParse(s.Substring(5, 4), out var year)) return false;

            try { d = new DateTime(year, month, day); return true; }
            catch { return false; }
        }

        //-------------------------------------------------------------
        // RFC 6238 TOTP from the SmartAPI base32 secret
        //-------------------------------------------------------------
        private static string GenerateTotp(string base32Secret) =>
            GenerateTotpAt(base32Secret, DateTime.UtcNow);

        private static string GenerateTotpAt(string base32Secret, DateTime utc)
        {
            var key = Base32Decode(base32Secret.Trim().Replace(" ", "").ToUpperInvariant());

            var unix = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var counter = BitConverter.GetBytes(unix / 30);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counter);

            int offset = hash[hash.Length - 1] & 0x0F;
            int code = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];

            return (code % 1000000).ToString("D6");
        }

        private static byte[] Base32Decode(string s)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            s = s.TrimEnd('=');

            int bits = 0, value = 0;
            var output = new List<byte>();

            foreach (var c in s)
            {
                var idx = alphabet.IndexOf(c);
                if (idx < 0) continue;

                value = (value << 5) | idx;
                bits += 5;

                if (bits >= 8)
                {
                    output.Add((byte)((value >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }

            return output.ToArray();
        }
    }
}
