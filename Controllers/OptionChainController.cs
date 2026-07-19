using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using StockWebApplications;
using StockWebApplications.Models;
using System.Globalization;
using System.Net;


public class OptionChainController : Controller
{
    private readonly IMemoryCache _cache;
    private readonly AngelOneClient _angel;
    private readonly DataAccess _dataAccess = new DataAccess();

    public OptionChainController(IMemoryCache cache, AngelOneClient angel)
    {
        _cache = cache;
        _angel = angel;
    }

    private static readonly HttpClient client = new HttpClient();

    private static readonly HttpClientHandler nseHandler = new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        UseCookies = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };
    private static readonly HttpClient nseClient = new HttpClient(nseHandler);
    private static DateTime nseCookiesFetchedAt = DateTime.MinValue;

    private static readonly HashSet<string> IndexSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "NIFTY", "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY", "NIFTYNXT50"
    };

    private static bool IsIndex(string symbol) => IndexSymbols.Contains(symbol?.Trim() ?? "");

    private async Task<string> GetYahooData(string url)
    {
        client.DefaultRequestHeaders.Clear();

        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");

        client.DefaultRequestHeaders.Add("Accept",
            "application/json, text/plain, */*");

        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

        client.DefaultRequestHeaders.Add("Connection", "keep-alive");

        // 🔥 IMPORTANT: Hit base domain first (sets cookies)
        await client.GetAsync("https://finance.yahoo.com/");

        await Task.Delay(500); // small delay

        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync(url);

            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 429)
            {
                await Task.Delay(1500);
                continue;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        throw new Exception("Yahoo API blocked request. Try again.");
    }
    public async Task<IActionResult> Index(string symbol = "^NSEI", long? expiry = null)
    {
        string cacheKey = $"{symbol}_{expiry}";

        if (!_cache.TryGetValue(cacheKey, out OptionChainViewModel model))
        {
            model = new OptionChainViewModel
            {
                IndexName = symbol,
                Data = new List<OptionData>()
            };

            // STEP 1: Get expiry list
            var url = $"https://query2.finance.yahoo.com/v7/finance/options/{symbol}";
            var json = JObject.Parse(await GetYahooData(url));

            var result = json["optionChain"]["result"][0];

            model.ExpiryDates = result["expirationDates"]
                .Select(x => (long)x)
                .ToList();

            model.SelectedExpiry = expiry ?? model.ExpiryDates.First();

            // STEP 2: Get selected expiry data
            var url2 = $"https://query2.finance.yahoo.com/v7/finance/options/{symbol}?date={model.SelectedExpiry}";
            var json2 = JObject.Parse(await GetYahooData(url2));

            var result2 = json2["optionChain"]["result"][0];

            var options = result2["options"][0];
            var calls = options["calls"];
            var puts = options["puts"];

            model.SpotPrice = result2["quote"]["regularMarketPrice"].Value<decimal>();

            decimal maxCallVol = 0;
            decimal maxPutVol = 0;

            for (int i = 0; i < calls.Count(); i++)
            {
                var call = calls[i];
                var put = puts[i];

                var strike = call["strike"].Value<decimal>();
                var callVol = call["volume"]?.Value<decimal>() ?? 0;
                var putVol = put["volume"]?.Value<decimal>() ?? 0;

                if (callVol > maxCallVol)
                {
                    maxCallVol = callVol;
                    model.MaxCallVolumeStrike = strike;
                }

                if (putVol > maxPutVol)
                {
                    maxPutVol = putVol;
                    model.MaxPutVolumeStrike = strike;
                }

                model.Data.Add(new OptionData
                {
                    StrikePrice = strike,

                    CallLTP = call["lastPrice"].Value<decimal>(),
                    CallChangePercent = call["percentChange"].Value<decimal>(),
                    CallVolume = callVol,

                    PutLTP = put["lastPrice"].Value<decimal>(),
                    PutChangePercent = put["percentChange"].Value<decimal>(),
                    PutVolume = putVol
                });
            }

            // 🔥 Cache for 30 sec
            _cache.Set(cacheKey, model, TimeSpan.FromSeconds(30));
        }

        return View(model);
    }

    // ------------------------------------------------------------
    // Live LTP + P/L per leg for a given strategy (NSE India source)
    // ------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetLegLtps(int strategyId)
    {
        try
        {
            var dashboard = _dataAccess.GetStrategies();
            var strategy = dashboard.Masters.FirstOrDefault(m => m.StrategyId == strategyId);

            if (strategy == null)
            {
                return Json(new { legs = Array.Empty<object>() });
            }

            var legs = dashboard.Legs.Where(l => l.StrategyId == strategyId).ToList();
            var symbol = (strategy.Symbol ?? "").Trim().ToUpperInvariant();

            // Primary source: Angel One SmartAPI (broker feed, accurate LTPs).
            // Falls back to the NSE website source when not configured or on failure.
            Dictionary<int, decimal?> angelLtps = null;

            if (_angel.IsConfigured)
            {
                try
                {
                    angelLtps = await _angel.GetLegLtpsAsync(symbol, legs, strategy.ExpiryDate);
                }
                catch
                {
                    angelLtps = null;
                }
            }

            // Legs can carry their own expiry; fall back to the strategy-level one.
            // Fetch one NSE chain per distinct option-leg expiry.
            // Skipped entirely when Angel One supplied the prices.
            var priceMaps = new Dictionary<DateTime, Dictionary<decimal, (decimal? Ce, decimal? Pe)>>();

            var optionExpiries = angelLtps != null
                ? Enumerable.Empty<DateTime>()
                : legs
                .Where(l => string.Equals(l.InstrumentType, "CE", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(l.InstrumentType, "PE", StringComparison.OrdinalIgnoreCase))
                .Select(l => (l.ExpiryDate ?? strategy.ExpiryDate).Date)
                .Distinct();

            foreach (var expiry in optionExpiries)
            {
                // Pre-migration legs have no expiry anywhere — nothing to fetch for them.
                if (expiry.Year < 2000) continue;

                var cacheKey = $"nse_oc_{symbol}_{expiry:yyyy-MM-dd}";

                if (!_cache.TryGetValue(cacheKey, out Dictionary<decimal, (decimal? Ce, decimal? Pe)> priceMap))
                {
                    priceMap = await FetchNseOptionChainAsync(symbol, expiry);
                    _cache.Set(cacheKey, priceMap, TimeSpan.FromSeconds(8));
                }

                priceMaps[expiry] = priceMap;
            }

            // FUTURE legs: equity quote API for stocks; option-chain underlyingValue for indices.
            decimal? futureLtp = null;

            if (angelLtps == null
                && legs.Any(l => string.Equals(l.InstrumentType, "FUTURE", StringComparison.OrdinalIgnoreCase)))
            {
                var futCacheKey = $"nse_fut_{symbol}";

                if (!_cache.TryGetValue(futCacheKey, out futureLtp))
                {
                    if (!IsIndex(symbol))
                        futureLtp = await FetchNseEquityLtpAsync(symbol);

                    if (futureLtp == null)
                        _cache.TryGetValue($"nse_underlying_{symbol}", out futureLtp);

                    if (futureLtp == null)
                    {
                        // No option legs fetched a chain yet — fetch one for the future's expiry
                        // just to obtain underlyingValue.
                        var futExpiry = legs
                            .Where(l => string.Equals(l.InstrumentType, "FUTURE", StringComparison.OrdinalIgnoreCase))
                            .Select(l => (l.ExpiryDate ?? strategy.ExpiryDate).Date)
                            .FirstOrDefault(e => e.Year >= 2000);

                        if (futExpiry != default)
                        {
                            await FetchNseOptionChainAsync(symbol, futExpiry);
                            _cache.TryGetValue($"nse_underlying_{symbol}", out futureLtp);
                        }
                    }

                    _cache.Set(futCacheKey, futureLtp, TimeSpan.FromSeconds(8));
                }
            }

            var result = legs.Select(leg =>
            {
                decimal? ltp = null;
                var legExpiry = (leg.ExpiryDate ?? strategy.ExpiryDate).Date;

                if (angelLtps != null)
                {
                    angelLtps.TryGetValue(leg.LegId, out ltp);
                }
                else if (string.Equals(leg.InstrumentType, "FUTURE", StringComparison.OrdinalIgnoreCase))
                {
                    ltp = futureLtp;
                }
                else if (leg.StrikePrice.HasValue
                    && priceMaps.TryGetValue(legExpiry, out var priceMap)
                    && priceMap.TryGetValue(leg.StrikePrice.Value, out var pair))
                {
                    if (string.Equals(leg.InstrumentType, "CE", StringComparison.OrdinalIgnoreCase))
                        ltp = pair.Ce;
                    else if (string.Equals(leg.InstrumentType, "PE", StringComparison.OrdinalIgnoreCase))
                        ltp = pair.Pe;
                }

                decimal? pl = null;
                if (ltp.HasValue)
                {
                    var sign = string.Equals(leg.ActionType, "BUY", StringComparison.OrdinalIgnoreCase) ? 1m : -1m;
                    pl = (ltp.Value - leg.TradePrice) * leg.Quantity * sign;
                }

                return new
                {
                    LegId = leg.LegId,
                    LTP = ltp,
                    PL = pl
                };
            }).ToList();

            return Json(new { legs = result });
        }
        catch (Exception ex)
        {
            return Json(new { legs = Array.Empty<object>(), error = ex.Message });
        }
    }

    private async Task<Dictionary<decimal, (decimal? Ce, decimal? Pe)>> FetchNseOptionChainAsync(string symbol, DateTime expiry)
    {
        await EnsureNseCookiesAsync();

        var expiryParam = expiry.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        var typeParam = IsIndex(symbol) ? "Indices" : "Equity";
        var apiPath = $"https://www.nseindia.com/api/option-chain-v3?type={typeParam}&symbol={Uri.EscapeDataString(symbol)}&expiry={Uri.EscapeDataString(expiryParam)}";

        string body = null;
        for (int i = 0; i < 3; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, apiPath);
            req.Headers.TryAddWithoutValidation("Referer", "https://www.nseindia.com/option-chain");
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            var resp = await nseClient.SendAsync(req);
            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
            {
                nseCookiesFetchedAt = DateTime.MinValue;
                await EnsureNseCookiesAsync(force: true);
                await Task.Delay(500);
                continue;
            }
            resp.EnsureSuccessStatusCode();
            body = await resp.Content.ReadAsStringAsync();
            break;
        }

        var map = new Dictionary<decimal, (decimal? Ce, decimal? Pe)>();
        if (string.IsNullOrEmpty(body)) return map;

        var json = JObject.Parse(body);

        // v3 shape: records.data[] with CE / PE nested, already filtered by expiry.
        // filtered.data[] is an alternative in some responses.
        var data = json["records"]?["data"] as JArray
                   ?? json["filtered"]?["data"] as JArray;
        if (data == null) return map;

        decimal? underlying = null;

        foreach (var row in data)
        {
            var strikeToken = row["strikePrice"] ?? row["CE"]?["strikePrice"] ?? row["PE"]?["strikePrice"];
            if (strikeToken == null) continue;
            var strike = strikeToken.Value<decimal>();

            decimal? ce = row["CE"]?["lastPrice"]?.Value<decimal?>();
            decimal? pe = row["PE"]?["lastPrice"]?.Value<decimal?>();

            if (underlying == null)
                underlying = row["CE"]?["underlyingValue"]?.Value<decimal?>()
                          ?? row["PE"]?["underlyingValue"]?.Value<decimal?>();

            // If both entries for this strike appear across multiple rows, keep whichever side is populated.
            if (map.TryGetValue(strike, out var existing))
            {
                map[strike] = (ce ?? existing.Ce, pe ?? existing.Pe);
            }
            else
            {
                map[strike] = (ce, pe);
            }
        }

        if (underlying != null)
            _cache.Set($"nse_underlying_{symbol}", underlying, TimeSpan.FromSeconds(8));

        return map;
    }

    private async Task<decimal?> FetchNseEquityLtpAsync(string symbol)
    {
        await EnsureNseCookiesAsync();

        var apiPath = $"https://www.nseindia.com/api/NextApi/apiClient/GetQuoteApi?functionName=getSymbolData&marketType=N&series=EQ&symbol={Uri.EscapeDataString(symbol)}";

        string body = null;
        for (int i = 0; i < 3; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, apiPath);
            req.Headers.TryAddWithoutValidation("Referer", $"https://www.nseindia.com/get-quote/derivatives/{Uri.EscapeDataString(symbol)}");
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            var resp = await nseClient.SendAsync(req);
            if ((int)resp.StatusCode == 401 || (int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
            {
                nseCookiesFetchedAt = DateTime.MinValue;
                await EnsureNseCookiesAsync(force: true);
                await Task.Delay(500);
                continue;
            }
            if (!resp.IsSuccessStatusCode) return null;
            body = await resp.Content.ReadAsStringAsync();
            break;
        }

        if (string.IsNullOrEmpty(body)) return null;

        try
        {
            var json = JObject.Parse(body);
            var eq = json["equityResponse"]?[0];

            return eq?["orderBook"]?["lastPrice"]?.Value<decimal?>()
                ?? eq?["tradeInfo"]?["lastPrice"]?.Value<decimal?>();
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureNseCookiesAsync(bool force = false)
    {
        if (!force && (DateTime.UtcNow - nseCookiesFetchedAt).TotalMinutes < 5) return;

        nseClient.DefaultRequestHeaders.Clear();
        nseClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        nseClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        nseClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        nseClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        nseClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

        await nseClient.GetAsync("https://www.nseindia.com/");
        await Task.Delay(300);
        await nseClient.GetAsync("https://www.nseindia.com/option-chain");
        await Task.Delay(300);

        nseCookiesFetchedAt = DateTime.UtcNow;
    }
}