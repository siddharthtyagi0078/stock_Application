using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StockWebApplications.Models;
using StockWebApplications.Services;

namespace StockWebApplications.Controllers
{
    // RAG Knowledge Assistant. A router first classifies the question:
    //   - stock_price  -> live Yahoo quote (DataAccess.GetLiveStockAsync)
    //   - option_price -> live Angel One LTP (AngelOneClient.GetLegLtpsAsync)
    //   - sql          -> text-to-SQL over the trading DB (read-only)
    //   - none         -> polite refusal
    // Live prices come from market feeds; everything else is answered from the DB.
    public class AssistantController : Controller
    {
        private readonly DataAccess _data = new DataAccess();
        private readonly GeminiClient _ai;
        private readonly AngelOneClient _angel;

        public AssistantController(IConfiguration config, AngelOneClient angel)
        {
            _ai = new GeminiClient(config);
            _angel = angel;
        }

        public IActionResult Index() => View();

        // A previous turn asked the user to disambiguate a field. Round-tripped
        // via the bot UI so the follow-up carries the original intent.
        public class PendingContext
        {
            public string? action { get; set; }             // e.g. "add_leg"
            public string? originalQuestion { get; set; }   // the user's original ask
            public string? ambiguousField { get; set; }     // e.g. "strategyName"
        }

        public class AskRequest
        {
            public string? Question { get; set; }
            public PendingContext? Pending { get; set; }
        }

        [HttpPost]
        public async Task<JsonResult> Ask([FromBody] AskRequest req)
        {
            var question = req?.Question?.Trim() ?? "";
            if (question.Length == 0)
                return Json(new { success = false, error = "Please enter a question." });

            if (!_ai.IsConfigured)
                return Json(new { success = false, error = "AI is not configured (missing AiSummary:ApiKey)." });

            // Follow-up to a "which one?" prompt — treat this reply as the disambiguation
            // for the ambiguous field, then re-classify the augmented question.
            var pending = req?.Pending;
            if (pending != null && !string.IsNullOrWhiteSpace(pending.originalQuestion))
            {
                var reply = question;
                var origQ = pending.originalQuestion!;
                if (string.Equals(pending.ambiguousField, "strategyName", StringComparison.OrdinalIgnoreCase))
                    question = $"The strategy is exactly \"{reply}\". {origQ}";
                else
                    question = $"{reply}. {origQ}";
            }

            // 1) Route the (possibly augmented) question.
            var route = await ClassifyAsync(question);
            var action = route?["action"]?.ToString()?.ToLowerInvariant() ?? "sql";

            try
            {
                switch (action)
                {
                    case "stock_price":    return await AnswerStockPrice(route!);
                    case "option_price":   return await AnswerOptionPrice(route!);
                    // Writes never execute here — they return a confirmation the user must approve.
                    case "add_stock":      return PrepareAddStock(route!);
                    case "sell_stock":     return PrepareSellStock(route!);
                    case "add_tracker":    return PrepareAddTracker(route!);
                    case "update_comment": return PrepareUpdateComment(route!, question);
                    case "add_leg":        return PrepareAddLeg(route!, question);
                    case "edit_leg":       return PrepareEditLeg(route!, question);
                    case "delete_leg":     return PrepareDeleteLeg(route!, question);
                    case "exit_leg":       return PrepareExitLeg(route!, question);
                    case "none":
                        return Json(new { success = false, error = "I can answer questions about your trades, P/L, accounts, strategies, and live stock/option prices — and add/sell/track stocks, update strategy comments, or add/edit/delete/exit option-strategy legs (with confirmation)." });
                    default:               return await AnswerViaSql(question);
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        //--------------------------------------------------------------------
        // Router: ask the model to classify + extract parameters as JSON.
        //--------------------------------------------------------------------
        private async Task<JObject?> ClassifyAsync(string question)
        {
            var today = DateTime.Now;
            var preamble = $"Today's date is {today:yyyy-MM-dd} ({today:dddd}). The current year is {today:yyyy}.\n";
            var prompt = preamble + @"Classify the user's question and extract parameters. Output ONLY compact JSON, no markdown.
Schema:
{
  ""action"": ""stock_price"" | ""option_price"" | ""sql"" | ""add_stock"" | ""sell_stock"" | ""add_tracker"" | ""update_comment"" | ""add_leg"" | ""edit_leg"" | ""delete_leg"" | ""exit_leg"" | ""none"",
  ""symbol"": ""<stock ticker, e.g. RELIANCE>"",
  ""underlying"": ""<option underlying, e.g. NIFTY, BANKNIFTY, SENSEX; default NIFTY if not stated>"",
  ""strike"": <number, option strike>,
  ""optionType"": ""CE"" | ""PE"" | ""FUTURE"",
  ""expiry"": ""YYYY-MM-DD"",
  ""quantity"": <int, shares to buy (add_stock) OR leg lots/qty (add_leg/edit_leg)>,
  ""price"": <number, buy price (add_stock) OR trade price for the leg (add_leg/edit_leg)>,
  ""sellPrice"": <number, sell price per share (sell_stock)>,
  ""exitPrice"": <number, exit price for an option leg (exit_leg)>,
  ""account"": ""Arnav-Angelone"" | ""Sid-Connect"" | ""Archana-Angelone"",
  ""id"": <int, explicit Shares row id (sell_stock, optional)>,
  ""legId"": <int, explicit OptionStrategyLeg id (edit_leg/delete_leg/exit_leg, optional)>,
  ""actionType"": ""BUY"" | ""SELL"" (add_leg/edit_leg leg direction),
  ""newStrike"": <number, replacement strike (edit_leg)>,
  ""newPrice"": <number, replacement trade price (edit_leg)>,
  ""newQuantity"": <int, replacement quantity (edit_leg)>,
  ""newExpiry"": ""YYYY-MM-DD"" (edit_leg),
  ""targetPrice"": <number, add_tracker>,
  ""targetDate"": ""YYYY-MM-DD"" (add_tracker),
  ""notes"": ""<text, add_tracker>"",
  ""strategyName"": ""<option strategy name (update_comment/add_leg/edit_leg/delete_leg/exit_leg)>"",
  ""comment"": ""<new comment text (update_comment)>""
}
Rules:
- 'price'/'ltp'/'quote'/'trading at' for a single share/ETF -> stock_price.
- an option (strike with CE/PE/call/put and usually expiry) asking a PRICE -> option_price.
- BUY / add / purchase a stock into the portfolio -> add_stock.
- SELL / exit / book / close a stock position -> sell_stock.
- watch / track / set a target for a stock -> add_tracker.
- set / update / change the comment/remark/note on an option strategy -> update_comment.
- 'add leg', 'add a new leg', 'insert leg' to/into a strategy -> add_leg.
- 'edit leg', 'change leg', 'update leg', 'modify leg' (strike/price/qty/expiry) -> edit_leg.
- 'delete leg', 'remove leg', 'drop leg' from a strategy -> delete_leg.
- 'exit leg', 'close leg', 'square off leg' at a price -> exit_leg.
- anything READING the user's own trades, holdings, P/L, accounts, strategies, counts, history -> sql.
- unrelated to trading/markets -> none.
- For dates, use the CURRENT year when the user omits the year. If that date already passed this year, use next year. Never use a past year.
- For add_leg / edit_leg / delete_leg / exit_leg: extract strategyName (the strategy the leg belongs to; may be a partial name like 'sid-hdfc'). Legs are identified either by legId, or by (strategyName + strike + optionType).
- Only include the fields relevant to the chosen action.

QUESTION: " + question + "\nJSON:";

            var raw = await _ai.GenerateAsync(prompt);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Strip code fences / surrounding text, keep the JSON object.
            int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
            if (a >= 0 && b > a) raw = raw.Substring(a, b - a + 1);
            try { return JObject.Parse(raw); }
            catch { return null; }
        }

        //--------------------------------------------------------------------
        // Live stock/ETF quote (Yahoo).
        //--------------------------------------------------------------------
        private async Task<JsonResult> AnswerStockPrice(JObject route)
        {
            var symbol = route["symbol"]?.ToString()?.Trim() ?? "";
            if (symbol.Length == 0)
                return Json(new { success = false, error = "Which stock? Please include the symbol." });

            StockModel q = await _data.GetLiveStockAsync(symbol);
            if (q == null || q.close <= 0)
                return Json(new { success = false, error = $"Couldn't fetch a live price for '{symbol}'. Check the symbol." });

            var name = string.IsNullOrWhiteSpace(q.companyName) ? symbol.ToUpperInvariant() : q.companyName;
            var arrow = q.change >= 0 ? "▲" : "▼";
            var answer =
                $"{name} is trading at Rs {q.close:N2} {arrow} {q.change:+0.00;-0.00} ({q.changepercent:+0.00;-0.00}%). " +
                $"Day range Rs {q.low:N2}–{q.high:N2}.";
            return Json(new { success = true, answer, source = "Live quote (Yahoo)" });
        }

        //--------------------------------------------------------------------
        // Live option LTP (Angel One SmartAPI).
        //--------------------------------------------------------------------
        private async Task<JsonResult> AnswerOptionPrice(JObject route)
        {
            var underlying = route["underlying"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(underlying)) underlying = "NIFTY";

            var typeStr = route["optionType"]?.ToString()?.Trim().ToUpperInvariant();
            if (typeStr != "CE" && typeStr != "PE")
                return Json(new { success = false, error = "Is it a CE or PE? Please specify." });

            if (route["strike"] == null || !decimal.TryParse(route["strike"]!.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var strike))
                return Json(new { success = false, error = "What strike price? Please include it." });

            if (route["expiry"] == null ||
                !DateTime.TryParse(route["expiry"]!.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                return Json(new { success = false, error = "Which expiry date? Please include it (e.g. 25-Sep-2026)." });

            var leg = new StrategyLegVM
            {
                LegId = 1,
                InstrumentType = typeStr,
                StrikePrice = strike,
                ExpiryDate = expiry.Date
            };

            Dictionary<int, decimal?> ltps;
            try
            {
                ltps = await _angel.GetLegLtpsAsync(underlying.ToUpperInvariant(), new List<StrategyLegVM> { leg }, expiry.Date);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Live option feed unavailable: " + ex.Message });
            }

            var contract = $"{underlying.ToUpperInvariant()} {strike:0.##} {typeStr} {expiry:dd-MMM-yyyy}";
            if (ltps.TryGetValue(1, out var ltp) && ltp.HasValue)
                return Json(new { success = true, answer = $"{contract} — LTP Rs {ltp.Value:N2}.", source = "Live LTP (Angel One)" });

            return Json(new
            {
                success = false,
                error = $"No live price found for {contract}. Check the underlying (NIFTY/BANKNIFTY/SENSEX), strike and expiry — that contract may not exist or the Angel One session/IP isn't active."
            });
        }

        //====================================================================
        //  WRITE ACTIONS  (two-phase: confirm here, execute in Execute())
        //  The AI only fills parameters — writes go through the app's vetted
        //  stored procedures, never free-form SQL, and only after the user
        //  approves the confirmation shown by the bot.
        //====================================================================
        public class WriteAction
        {
            public string? action { get; set; }
            public string? symbol { get; set; }
            public int? quantity { get; set; }
            public decimal? price { get; set; }
            public decimal? sellPrice { get; set; }
            public string? account { get; set; }
            public int? id { get; set; }
            public decimal? targetPrice { get; set; }
            public string? targetDate { get; set; }
            public string? notes { get; set; }
            public string? strategyName { get; set; }
            public int? strategyId { get; set; }
            public string? comment { get; set; }
            public string? date { get; set; }

            // Option-leg fields
            public int? legId { get; set; }
            public int? legNo { get; set; }
            public string? actionType { get; set; }     // BUY / SELL (leg direction)
            public string? instrumentType { get; set; } // CE / PE / FUTURE
            public decimal? strike { get; set; }
            public string? expiry { get; set; }         // YYYY-MM-DD
            public decimal? exitPrice { get; set; }
            public decimal? newStrike { get; set; }
            public decimal? newPrice { get; set; }
            public int? newQuantity { get; set; }
            public string? newExpiry { get; set; }
        }

        private static readonly string[] Accounts = { "Arnav-Angelone", "Sid-Connect", "Archana-Angelone" };

        private static string? S(JObject r, string k) => r[k]?.Type == JTokenType.Null ? null : r[k]?.ToString()?.Trim();
        private static decimal? D(JObject r, string k) =>
            decimal.TryParse(S(r, k), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;
        private static int? I(JObject r, string k) =>
            int.TryParse(S(r, k), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

        private JsonResult Confirm(WriteAction w, string summary) =>
            Json(new { success = true, confirm = true, action = w.action, write = w, summary });

        private JsonResult PrepareAddStock(JObject r)
        {
            var symbol = S(r, "symbol");
            var qty = I(r, "quantity");
            var price = D(r, "price");
            var account = S(r, "account");
            if (string.IsNullOrWhiteSpace(symbol)) return Json(new { success = false, error = "Which stock to add? Include the symbol." });
            if (qty is null or <= 0) return Json(new { success = false, error = "How many shares? Include a quantity." });
            if (price is null or <= 0) return Json(new { success = false, error = "At what buy price?" });
            if (!string.IsNullOrWhiteSpace(account) && !Accounts.Contains(account, StringComparer.OrdinalIgnoreCase))
                return Json(new { success = false, error = "Account must be one of: " + string.Join(", ", Accounts) + "." });

            var code = symbol.ToUpperInvariant();
            var w = new WriteAction { action = "add_stock", symbol = code, quantity = qty, price = price,
                                      account = account, date = DateTime.Now.ToString("MM/dd/yyyy") };
            var acctTxt = string.IsNullOrWhiteSpace(account) ? "" : $" [{account}]";
            return Confirm(w, $"Add BUY: {qty} {code} @ Rs {price:N2}{acctTxt} (dated {DateTime.Now:dd-MMM-yyyy}).");
        }

        private JsonResult PrepareSellStock(JObject r)
        {
            var symbol = S(r, "symbol");
            var sellPrice = D(r, "sellPrice");
            var explicitId = I(r, "id");
            if (string.IsNullOrWhiteSpace(symbol)) return Json(new { success = false, error = "Which stock to sell? Include the symbol." });
            if (sellPrice is null or <= 0) return Json(new { success = false, error = "At what sell price?" });

            var lots = _data.FindOpenShares(symbol);
            if (lots.Count == 0) return Json(new { success = false, error = $"No open position found for {symbol.ToUpperInvariant()}." });

            (int Id, string Code, int Shares, decimal Buy, string Account) lot;
            if (explicitId.HasValue)
            {
                var m = lots.Where(x => x.Id == explicitId.Value).ToList();
                if (m.Count == 0) return Json(new { success = false, error = $"Id {explicitId} is not an open {symbol.ToUpperInvariant()} lot." });
                lot = m[0];
            }
            else if (lots.Count > 1)
            {
                var listed = string.Join("; ", lots.Select(x => $"id {x.Id}: {x.Shares} @ Rs {x.Buy:N2}{(string.IsNullOrEmpty(x.Account) ? "" : " [" + x.Account + "]")}"));
                return Json(new { success = false, error = $"Multiple open lots for {lots[0].Code}: {listed}. Say which id to sell." });
            }
            else lot = lots[0];

            var pl = (sellPrice.Value - lot.Buy) * lot.Shares;
            var w = new WriteAction { action = "sell_stock", id = lot.Id, symbol = lot.Code, sellPrice = sellPrice };
            return Confirm(w, $"Sell {lot.Shares} {lot.Code} (id {lot.Id}, bought @ Rs {lot.Buy:N2}) at Rs {sellPrice:N2} — realized P/L Rs {pl:N2}.");
        }

        private JsonResult PrepareAddTracker(JObject r)
        {
            var symbol = S(r, "symbol");
            if (string.IsNullOrWhiteSpace(symbol)) return Json(new { success = false, error = "Which stock to track? Include the symbol." });
            var tgt = D(r, "targetPrice");
            var tgtDate = S(r, "targetDate");
            var notes = S(r, "notes") ?? "";
            var code = symbol.ToUpperInvariant();

            var w = new WriteAction { action = "add_tracker", symbol = code, targetPrice = tgt,
                                      targetDate = string.IsNullOrWhiteSpace(tgtDate) ? DateTime.Now.ToString("MM/dd/yyyy") : tgtDate, notes = notes };
            var tgtTxt = tgt.HasValue ? $"target Rs {tgt:N2}" : "no target";
            var dateTxt = string.IsNullOrWhiteSpace(tgtDate) ? "" : $" by {tgtDate}";
            return Confirm(w, $"Track {code} ({tgtTxt}{dateTxt}){(string.IsNullOrWhiteSpace(notes) ? "" : $". Notes: {notes}")}.");
        }

        private JsonResult PrepareUpdateComment(JObject r, string originalQuestion)
        {
            var name = S(r, "strategyName");
            var comment = S(r, "comment");
            if (string.IsNullOrWhiteSpace(comment)) return Json(new { success = false, error = "What comment should I set?" });

            var errS = ResolveStrategy(name, originalQuestion, "update_comment", out var stratId, out var stratName);
            if (errS != null) return errS;

            var w = new WriteAction { action = "update_comment", strategyId = stratId, strategyName = stratName, comment = comment };
            return Confirm(w, $"Set comment on '{stratName}' (id {stratId}) to:\n{comment}");
        }

        //--------------------------------------------------------------------
        // Option-leg helpers
        //--------------------------------------------------------------------

        // Resolve the target strategy by name (must match exactly one). When more
        // than one matches (or none does), returns an error that also carries a
        // `pending` context — the bot UI stores it and sends it back with the
        // user's next reply so we can retry with the disambiguated name.
        private JsonResult? ResolveStrategy(string? name, string originalQuestion, string action, out int strategyId, out string strategyName)
        {
            strategyId = 0; strategyName = "";
            object Pending() => new { action, originalQuestion, ambiguousField = "strategyName" };

            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, error = "Which strategy? Give its name (e.g. 'Sid-HDFC').", clarify = true, pending = Pending() });
            var matches = _data.FindStrategyByName(name);
            if (matches.Count == 0)
                return Json(new { success = false, error = $"No strategy matches '{name}'. Which one? " + (name.Length > 0 ? "Reply with the exact name or a longer prefix." : ""), clarify = true, pending = Pending() });
            if (matches.Count > 1)
                return Json(new { success = false, error = "Multiple strategies match: " + string.Join("; ", matches.Select(m => $"{m.Name} (id {m.Id})")) + ". Reply with the exact name or its id.", clarify = true, pending = Pending() });
            strategyId = matches[0].Id; strategyName = matches[0].Name;
            return null;
        }

        // Find the target leg on a strategy by (strike + instrument type), or by explicit legId.
        // Returns the picked leg or a JsonResult error to send back.
        private (StrategyLegVM? leg, JsonResult? err) ResolveLeg(
            int? legId, int strategyId, decimal? strike, string? instrumentType)
        {
            var dash = _data.GetStrategies();
            var active = dash.Legs.Where(l => l.StrategyId == strategyId).ToList();
            if (active.Count == 0) return (null, Json(new { success = false, error = "That strategy has no active legs." }));

            if (legId.HasValue)
            {
                var m = active.FirstOrDefault(l => l.LegId == legId.Value);
                if (m == null) return (null, Json(new { success = false, error = $"Leg id {legId} is not an active leg on this strategy." }));
                return (m, null);
            }

            var instr = (instrumentType ?? "").Trim().ToUpperInvariant();
            var pool = active.AsEnumerable();
            if (strike.HasValue) pool = pool.Where(l => l.StrikePrice.HasValue && l.StrikePrice.Value == strike.Value);
            if (instr.Length > 0) pool = pool.Where(l => string.Equals(l.InstrumentType, instr, StringComparison.OrdinalIgnoreCase));
            var picks = pool.ToList();

            if (picks.Count == 0)
                return (null, Json(new { success = false, error = "No leg matches. Available: " +
                    string.Join("; ", active.Select(l => $"id {l.LegId} {l.ActionType} {l.StrikePrice?.ToString() ?? "-"} {l.InstrumentType}")) }));
            if (picks.Count > 1)
                return (null, Json(new { success = false, error = "Multiple legs match: " +
                    string.Join("; ", picks.Select(l => $"id {l.LegId} {l.ActionType} {l.StrikePrice?.ToString() ?? "-"} {l.InstrumentType}")) + ". Say which legId." }));
            return (picks[0], null);
        }

        private static string LegDesc(StrategyLegVM l) =>
            $"{l.ActionType} {(l.StrikePrice.HasValue ? l.StrikePrice.Value.ToString("0.##") : "-")} {l.InstrumentType} " +
            $"@ Rs {l.TradePrice:N2} qty {l.Quantity}" +
            (l.ExpiryDate.HasValue ? $" exp {l.ExpiryDate.Value:dd-MMM-yyyy}" : "");

        private JsonResult PrepareAddLeg(JObject r, string originalQuestion)
        {
            var name = S(r, "strategyName");
            var errS = ResolveStrategy(name, originalQuestion, "add_leg", out var stratId, out var stratName);
            if (errS != null) return errS;

            var actionType     = (S(r, "actionType") ?? "").ToUpperInvariant();
            var instrumentType = (S(r, "optionType") ?? S(r, "instrumentType") ?? "").ToUpperInvariant();
            var strike   = D(r, "strike");
            var price    = D(r, "price");
            var quantity = I(r, "quantity");
            var expiryS  = S(r, "expiry");

            if (actionType != "BUY" && actionType != "SELL")
                return Json(new { success = false, error = "Is the leg BUY or SELL?" });
            if (instrumentType != "CE" && instrumentType != "PE" && instrumentType != "FUTURE")
                return Json(new { success = false, error = "Leg type must be CE, PE or FUTURE." });
            if (instrumentType != "FUTURE" && (strike is null or <= 0))
                return Json(new { success = false, error = "What strike price?" });
            if (price is null or <= 0) return Json(new { success = false, error = "What trade price for the leg?" });
            if (quantity is null or <= 0) return Json(new { success = false, error = "What quantity for the leg?" });
            DateTime? exp = null;
            if (!string.IsNullOrWhiteSpace(expiryS))
            {
                if (!DateTime.TryParse(expiryS, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ex))
                    return Json(new { success = false, error = "Expiry must be YYYY-MM-DD." });
                exp = ex.Date;
            }

            var w = new WriteAction {
                action = "add_leg", strategyId = stratId, strategyName = stratName,
                actionType = actionType, instrumentType = instrumentType,
                strike = strike, price = price, quantity = quantity,
                expiry = exp?.ToString("yyyy-MM-dd")
            };
            var strikeTxt = instrumentType == "FUTURE" ? "" : $"{strike:0.##} ";
            var expTxt = exp.HasValue ? $" exp {exp:dd-MMM-yyyy}" : "";
            return Confirm(w, $"Add leg to '{stratName}': {actionType} {strikeTxt}{instrumentType} @ Rs {price:N2} qty {quantity}{expTxt}.");
        }

        private JsonResult PrepareEditLeg(JObject r, string originalQuestion)
        {
            var name = S(r, "strategyName");
            var errS = ResolveStrategy(name, originalQuestion, "edit_leg", out var stratId, out var stratName);
            if (errS != null) return errS;

            var legId = I(r, "legId");
            var strike = D(r, "strike");
            var instrumentType = (S(r, "optionType") ?? S(r, "instrumentType") ?? "").ToUpperInvariant();
            var (leg, errL) = ResolveLeg(legId, stratId, strike, instrumentType);
            if (errL != null) return errL;

            var newStrike   = D(r, "newStrike");
            var newPrice    = D(r, "newPrice");
            var newQuantity = I(r, "newQuantity");
            var newExpiryS  = S(r, "newExpiry");
            DateTime? newExp = null;
            if (!string.IsNullOrWhiteSpace(newExpiryS))
            {
                if (!DateTime.TryParse(newExpiryS, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ex))
                    return Json(new { success = false, error = "New expiry must be YYYY-MM-DD." });
                newExp = ex.Date;
            }
            if (newStrike is null && newPrice is null && newQuantity is null && newExp is null)
                return Json(new { success = false, error = "What should I change? Provide new strike, price, quantity, or expiry." });

            var changes = new List<string>();
            if (newStrike   != null) changes.Add($"strike {leg!.StrikePrice?.ToString("0.##") ?? "-"} -> {newStrike:0.##}");
            if (newPrice    != null) changes.Add($"price Rs {leg!.TradePrice:N2} -> Rs {newPrice:N2}");
            if (newQuantity != null) changes.Add($"qty {leg!.Quantity} -> {newQuantity}");
            if (newExp      != null) changes.Add($"exp {(leg!.ExpiryDate?.ToString("dd-MMM-yyyy") ?? "-")} -> {newExp:dd-MMM-yyyy}");

            var w = new WriteAction {
                action = "edit_leg", strategyId = stratId, strategyName = stratName,
                legId = leg!.LegId, newStrike = newStrike, newPrice = newPrice,
                newQuantity = newQuantity, newExpiry = newExp?.ToString("yyyy-MM-dd")
            };
            return Confirm(w, $"Edit leg id {leg.LegId} ({LegDesc(leg)}) in '{stratName}': " + string.Join(", ", changes) + ".");
        }

        private JsonResult PrepareDeleteLeg(JObject r, string originalQuestion)
        {
            var name = S(r, "strategyName");
            var errS = ResolveStrategy(name, originalQuestion, "delete_leg", out var stratId, out var stratName);
            if (errS != null) return errS;

            var legId = I(r, "legId");
            var strike = D(r, "strike");
            var instrumentType = (S(r, "optionType") ?? S(r, "instrumentType") ?? "").ToUpperInvariant();
            var (leg, errL) = ResolveLeg(legId, stratId, strike, instrumentType);
            if (errL != null) return errL;

            var w = new WriteAction { action = "delete_leg", strategyId = stratId, strategyName = stratName, legId = leg!.LegId };
            return Confirm(w, $"Delete leg id {leg.LegId} ({LegDesc(leg)}) from '{stratName}'. This is a hard delete.");
        }

        private JsonResult PrepareExitLeg(JObject r, string originalQuestion)
        {
            var name = S(r, "strategyName");
            var errS = ResolveStrategy(name, originalQuestion, "exit_leg", out var stratId, out var stratName);
            if (errS != null) return errS;

            var legId = I(r, "legId");
            var strike = D(r, "strike");
            var instrumentType = (S(r, "optionType") ?? S(r, "instrumentType") ?? "").ToUpperInvariant();
            var (leg, errL) = ResolveLeg(legId, stratId, strike, instrumentType);
            if (errL != null) return errL;

            var exit = D(r, "exitPrice") ?? D(r, "price") ?? D(r, "sellPrice");
            if (exit is null or < 0) return Json(new { success = false, error = "At what exit price?" });

            var w = new WriteAction { action = "exit_leg", strategyId = stratId, strategyName = stratName, legId = leg!.LegId, exitPrice = exit };
            return Confirm(w, $"Exit leg id {leg.LegId} ({LegDesc(leg)}) in '{stratName}' at Rs {exit:N2}.");
        }

        // Phase 2: actually perform the write. Only reached after the user confirms.
        [HttpPost]
        public JsonResult Execute([FromBody] WriteAction w)
        {
            if (w == null || string.IsNullOrWhiteSpace(w.action))
                return Json(new { success = false, error = "Nothing to execute." });
            try
            {
                switch (w.action.ToLowerInvariant())
                {
                    case "add_stock":
                        if (string.IsNullOrWhiteSpace(w.symbol) || w.quantity is null or <= 0 || w.price is null or <= 0)
                            return Json(new { success = false, error = "Missing add-stock details." });
                        _data.AddStock(new Models.AddStock
                        {
                            Script_code = w.symbol,
                            shares = w.quantity.ToString(),
                            inv_Price = w.price.Value.ToString(CultureInfo.InvariantCulture),
                            DateAdded = string.IsNullOrWhiteSpace(w.date) ? DateTime.Now.ToString("MM/dd/yyyy") : w.date,
                            Account = w.account
                        });
                        return Json(new { success = true, message = $"Added {w.quantity} {w.symbol!.ToUpperInvariant()} @ Rs {w.price:N2}." });

                    case "sell_stock":
                        if (w.id is null || w.sellPrice is null or <= 0)
                            return Json(new { success = false, error = "Missing sell details." });
                        _data.SellStock(w.id.Value.ToString(), w.sellPrice.Value.ToString(CultureInfo.InvariantCulture));
                        return Json(new { success = true, message = $"Sold lot id {w.id} at Rs {w.sellPrice:N2}." });

                    case "add_tracker":
                        if (string.IsNullOrWhiteSpace(w.symbol))
                            return Json(new { success = false, error = "Missing tracker symbol." });
                        _data.AddStocktracking(new Models.AddStocktracking
                        {
                            Script_code = w.symbol,
                            tgt_Price = (w.targetPrice ?? 0m).ToString(CultureInfo.InvariantCulture),
                            tgt_Date = string.IsNullOrWhiteSpace(w.targetDate) ? DateTime.Now.ToString("MM/dd/yyyy") : w.targetDate,
                            notes = w.notes ?? ""
                        });
                        return Json(new { success = true, message = $"Added {w.symbol!.ToUpperInvariant()} to the tracker." });

                    case "update_comment":
                        if (w.strategyId is null || string.IsNullOrWhiteSpace(w.comment))
                            return Json(new { success = false, error = "Missing comment details." });
                        _data.UpdateStrategyRemarks(w.strategyId.Value, w.comment);
                        return Json(new { success = true, message = $"Comment updated on strategy id {w.strategyId}." });

                    case "add_leg":
                        if (w.strategyId is null or <= 0 || string.IsNullOrWhiteSpace(w.actionType) ||
                            string.IsNullOrWhiteSpace(w.instrumentType) || w.price is null or <= 0 || w.quantity is null or <= 0)
                            return Json(new { success = false, error = "Missing add-leg details." });
                        var dash = _data.GetStrategies();
                        var existing = dash.Legs.Where(l => l.StrategyId == w.strategyId.Value).ToList();
                        var nextLegNo = existing.Count == 0 ? 1 : existing.Max(l => l.LegNo) + 1;
                        DateTime? legExp = null;
                        if (!string.IsNullOrWhiteSpace(w.expiry) &&
                            DateTime.TryParse(w.expiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out var eL))
                            legExp = eL.Date;
                        _data.InsertStrategyLeg(new OptionStrategyLegVM
                        {
                            StrategyId = w.strategyId.Value,
                            LegNo = nextLegNo,
                            ActionType = w.actionType,
                            InstrumentType = w.instrumentType,
                            StrikePrice = w.strike,
                            TradePrice = w.price.Value,
                            Quantity = w.quantity.Value,
                            ExpiryDate = legExp
                        });
                        return Json(new { success = true, message = $"Added leg {nextLegNo} to '{w.strategyName}'." });

                    case "edit_leg":
                        if (w.legId is null or <= 0)
                            return Json(new { success = false, error = "Missing leg id." });
                        DateTime? newExp = null;
                        if (!string.IsNullOrWhiteSpace(w.newExpiry) &&
                            DateTime.TryParse(w.newExpiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out var eE))
                            newExp = eE.Date;
                        _data.UpdateStrategyLeg(
                            legId: w.legId.Value,
                            strikePrice: w.newStrike,
                            tradePrice:  w.newPrice,
                            quantity:    w.newQuantity,
                            expiryDate:  newExp);
                        return Json(new { success = true, message = $"Updated leg id {w.legId}." });

                    case "delete_leg":
                        if (w.legId is null or <= 0)
                            return Json(new { success = false, error = "Missing leg id." });
                        _data.DeleteStrategyLeg(w.legId.Value);
                        return Json(new { success = true, message = $"Deleted leg id {w.legId} from '{w.strategyName}'." });

                    case "exit_leg":
                        if (w.legId is null or <= 0 || w.exitPrice is null or < 0)
                            return Json(new { success = false, error = "Missing exit-leg details." });
                        _data.ExitPosition(w.legId.Value, w.exitPrice.Value);
                        return Json(new { success = true, message = $"Exited leg id {w.legId} at Rs {w.exitPrice:N2}." });

                    default:
                        return Json(new { success = false, error = "Unknown action." });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "Write failed: " + ex.Message });
            }
        }

        //--------------------------------------------------------------------
        // Text-to-SQL over the trading DB (read-only).
        //--------------------------------------------------------------------
        private async Task<JsonResult> AnswerViaSql(string question)
        {
            var rawSql = await _ai.GenerateAsync(BuildSqlPrompt(question));
            if (string.IsNullOrWhiteSpace(rawSql))
                return Json(new { success = false, error = "The AI service did not respond. Try again." });

            if (rawSql.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, error = "I can only answer questions about your trades, P/L, accounts, strategies and live prices." });

            if (!SqlGuard.TryValidate(rawSql, out var sql, out var guardError))
                return Json(new { success = false, error = guardError, sql = rawSql });

            DataTable table;
            try { table = _data.RunReadOnlyQuery(sql); }
            catch (Exception ex) { return Json(new { success = false, error = "Query failed: " + ex.Message, sql }); }

            var (columns, rows) = Serialize(table);
            var answer = await _ai.GenerateAsync(BuildAnswerPrompt(question, sql, table)) ?? "Here are the results.";
            return Json(new { success = true, answer, sql, columns, rows });
        }

        private static string BuildSqlPrompt(string question)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You translate a user's question into ONE Microsoft SQL Server (T-SQL) SELECT query against the schema below.");
            sb.AppendLine("Output ONLY the SQL. No explanation, no markdown, no code fences.");
            sb.AppendLine("Rules:");
            sb.AppendLine("- A single SELECT statement only. Never INSERT/UPDATE/DELETE/DDL.");
            sb.AppendLine("- Use T-SQL: TOP (n) not LIMIT; GETDATE() for today.");
            sb.AppendLine("- Keep result sets small and relevant (aggregate where sensible).");
            sb.AppendLine("- If the question cannot be answered from this schema, output exactly: NONE");
            sb.AppendLine();
            sb.AppendLine("SCHEMA:");
            sb.AppendLine(DataAccess.SchemaKnowledge);
            sb.AppendLine("QUESTION: " + question);
            sb.AppendLine("SQL:");
            return sb.ToString();
        }

        private static string BuildAnswerPrompt(string question, string sql, DataTable table)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a helpful trading-data assistant. Answer the user's question using ONLY the query result rows below.");
            sb.AppendLine("Be concise and specific. Money is Indian Rupees — write amounts like Rs 38,631.53.");
            sb.AppendLine("If the result is empty, say no matching records were found. Do not invent numbers.");
            sb.AppendLine();
            sb.AppendLine("QUESTION: " + question);
            sb.AppendLine();
            sb.AppendLine("RESULT (" + table.Rows.Count + " row(s)):");
            sb.AppendLine(TableToText(table, 50));
            sb.AppendLine();
            sb.AppendLine("ANSWER:");
            return sb.ToString();
        }

        private static string TableToText(DataTable dt, int maxRows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(" | ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
            int n = Math.Min(dt.Rows.Count, maxRows);
            for (int i = 0; i < n; i++)
                sb.AppendLine(string.Join(" | ", dt.Rows[i].ItemArray.Select(v => v?.ToString() ?? "")));
            if (dt.Rows.Count > maxRows)
                sb.AppendLine($"... ({dt.Rows.Count - maxRows} more rows)");
            return sb.ToString();
        }

        private static (List<string> columns, List<List<string>> rows) Serialize(DataTable dt)
        {
            var columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
            var rows = new List<List<string>>();
            foreach (DataRow r in dt.Rows)
                rows.Add(r.ItemArray.Select(v => v == null || v == DBNull.Value ? "" : v.ToString() ?? "").ToList());
            return (columns, rows);
        }
    }
}
