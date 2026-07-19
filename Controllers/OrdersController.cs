using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StockWebApplications.Models;
using System.Text;

namespace StockWebApplications.Controllers
{
    public class OrdersController : Controller
    {

        DataAccess dataAccess = new DataAccess();

        private readonly IConfiguration _config;
        private static readonly HttpClient aiClient = new HttpClient();

        public OrdersController(IConfiguration config)
        {
            _config = config;
        }
        public IActionResult Index()
        {
            //var data = dataAccess.GetTradeReport();

            //// Summary calculation
            //ViewBag.FutureProfit = data.Count(x => x.Type == "FUTURE" && x.Status == "Profit");
            //ViewBag.FutureLoss = data.Count(x => x.Type == "FUTURE" && x.Status == "Loss");

            //ViewBag.OptionProfit = data.Count(x => x.Type == "OPTION" && x.Status == "Profit");
            //ViewBag.OptionLoss = data.Count(x => x.Type == "OPTION" && x.Status == "Loss");
          
            //// ✅ Overall Profit/Loss
            //ViewBag.TotalPL = data
            //    .Where(x => x.ProfitLossValue.HasValue)
            //    .Sum(x => x.ProfitLossValue.Value);

            return View();
        }

        public ActionResult TradeReport()
        {
            var data = dataAccess.GetTradeReport();

            // Summary calculation
            ViewBag.FutureProfit = data.Count(x => x.Type == "FUTURE" && x.Status == "Profit");
            ViewBag.FutureLoss = data.Count(x => x.Type == "FUTURE" && x.Status == "Loss");

            ViewBag.OptionProfit = data.Count(x => x.Type == "OPTION" && x.Status == "Profit");
            ViewBag.OptionLoss = data.Count(x => x.Type == "OPTION" && x.Status == "Loss");

            return View(data);
        }

        [HttpPost]
        public async Task<JsonResult> Create(OptionStrategyVM model)
        {
            try
            {
                if (model == null)
                {
                    return Json(new
                    {
                        Success = false,
                        Message = "Model is NULL"
                    });
                }

                int strategyId = dataAccess.InsertStrategy(model);

                foreach (var leg in model.Legs)
                {
                    leg.StrategyId = strategyId;
                    dataAccess.InsertStrategyLeg(leg);
                }

                // AI summary of the strategy, stored as the comment (Remarks).
                // Failure here must never fail the save.
                string aiSummary = null;
                try
                {
                    aiSummary = await GetAiSummaryAsync(model);

                    if (!string.IsNullOrWhiteSpace(aiSummary))
                    {
                        var remarks = string.IsNullOrWhiteSpace(model.Remarks)
                            ? aiSummary
                            : model.Remarks + "\n\nAI Summary:\n" + aiSummary;

                        dataAccess.UpdateStrategyRemarks(strategyId, remarks);
                    }
                }
                catch
                {
                    // ignore — strategy is already saved
                }

                return Json(new
                {
                    Success = true,
                    StrategyName = model.StrategyName,
                    LegsCount = model.Legs == null ? 0 : model.Legs.Count,
                    Summary = aiSummary
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        //-------------------------------------------------------------
        // AI summary (Google Gemini free tier)
        // Key + model configured in appsettings.json -> AiSummary
        //-------------------------------------------------------------
        private string BuildAiPrompt(OptionStrategyVM model)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an options trading analyst. Output an options-strategy Key Metrics report using EXACTLY the eleven labeled lines below, in this order, one per line, and nothing else (no greetings, no disclaimers, no preamble, no closing text, no headings, no bullets, no tables).");
            sb.AppendLine("**Strategy:** <name it, e.g. Covered Call (via Futures), Bull Call Spread, Iron Condor, Short Strangle>");
            sb.AppendLine("**Total Premium Received:** Rs. <net per-unit premium collected, two decimals> per unit  (write 'Rs. <amount> per unit (net debit)' for net-debit strategies)");
            sb.AppendLine("**Maximum Profit:** Rs. <total-position max profit with Indian-style commas and two decimals>");
            sb.AppendLine("**Profit Zone:** <underlying-price range where the trade is in profit at expiry, e.g. 23,769.36 to 24,530.64; write N/A if not a range>");
            sb.AppendLine("**Lower Break-even:** <underlying price, two decimals; write N/A if this strategy has only one breakeven>");
            sb.AppendLine("**Upper Break-even:** <underlying price, two decimals; write N/A if this strategy has only one breakeven>");
            sb.AppendLine("**Ideal Market View:** <Range-bound / Bullish / Moderately Bullish / Bearish / Moderately Bearish / Volatility-long / Volatility-short>");
            sb.AppendLine("**Time Decay (Theta):** <Positive (works in your favor) / Negative (works against you) / Mixed>");
            sb.AppendLine("**Volatility (Vega):** <Negative (fall in IV helps) / Positive (rise in IV helps) / Mixed>");
            sb.AppendLine("**Maximum Loss:** <e.g. 'Rs. 6,68,405.50 (if LT goes to zero)' or 'Unlimited above 24,530.64 or below 23,769.36'>");
            sb.AppendLine("**Profitable Range:** <the sold-strike / max-profit range at expiry, e.g. 23,950 to 24,350; write N/A if it does not apply>");
            sb.AppendLine("Rules:");
            sb.AppendLine("- Wrap ONLY the label text (words before ':') in ** markdown for bold. No other bold, italics, bullets, headings, or markdown anywhere.");
            sb.AppendLine("- Use 'Rs.' (not the rupee symbol). Use Indian-style comma grouping (e.g. 6,68,405.50).");
            sb.AppendLine("- Compute Maximum Profit / Maximum Loss on the TOTAL position (per-unit P&L multiplied by quantity), not per unit. Total Premium Received stays per unit.");
            sb.AppendLine("- If a field does not apply to this strategy, still emit the line and write 'N/A' as the value. Never drop a line or reorder lines.");
            sb.AppendLine("- Keep the whole output under 900 characters.");

            foreach (var leg in model.Legs)
            {
                var expiry = (leg.ExpiryDate ?? model.ExpiryDate).ToString("dd-MMM-yyyy");

                if (string.Equals(leg.InstrumentType, "FUTURE", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{leg.ActionType} FUTURE {model.Symbol} at {leg.TradePrice} lot size {leg.Quantity} expiry {expiry}");
                }
                else
                {
                    sb.AppendLine($"{leg.ActionType} {leg.InstrumentType} for {leg.StrikePrice} {model.Symbol} at {leg.TradePrice} lot size {leg.Quantity} expiry {expiry}");
                }
            }

            return sb.ToString();
        }

        private async Task<string> GetAiSummaryAsync(OptionStrategyVM model)
        {
            var apiKey = _config["AiSummary:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            var aiModel = _config["AiSummary:Model"];
            if (string.IsNullOrWhiteSpace(aiModel)) aiModel = "gemini-3.5-flash";

            var prompt = BuildAiPrompt(model);

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{aiModel}:generateContent?key={apiKey}";

            var payload = new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["parts"] = new JArray
                        {
                            new JObject { ["text"] = prompt }
                        }
                    }
                }
            };

            using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var resp = await aiClient.PostAsync(url, content, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            var json = JObject.Parse(body);

            return json["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>()?.Trim();
        }

        // Rebuild the AI comment from the strategy's CURRENT legs after any modification.
        // Never throws — a failed AI call must not fail the add/remove itself.
        private async Task RegenerateAiRemarksAsync(int strategyId)
        {
            try
            {
                var dashboard = dataAccess.GetStrategies();
                var master = dashboard.Masters.FirstOrDefault(m => m.StrategyId == strategyId);

                if (master == null || master.Legs == null || master.Legs.Count == 0) return;

                var vm = new OptionStrategyVM
                {
                    Symbol = master.Symbol,
                    ExpiryDate = master.ExpiryDate,
                    Legs = master.Legs.Select(l => new OptionStrategyLegVM
                    {
                        ActionType = l.ActionType,
                        InstrumentType = l.InstrumentType,
                        StrikePrice = l.StrikePrice,
                        ExpiryDate = l.ExpiryDate,
                        TradePrice = l.TradePrice,
                        Quantity = l.Quantity
                    }).ToList()
                };

                var summary = await GetAiSummaryAsync(vm);
                if (string.IsNullOrWhiteSpace(summary)) return;

                dataAccess.UpdateStrategyRemarks(strategyId, summary);
            }
            catch
            {
                // ignore — the leg change already succeeded
            }
        }

        [HttpGet]
        public JsonResult GetStrategies()
        {
            try
            {
                var dashboard = dataAccess.GetStrategies();
                return Json(dashboard.Masters);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<JsonResult> AddLeg(OptionStrategyLegVM model)
        {
            try
            {
                if (model == null || model.StrategyId <= 0)
                {
                    return Json(new { success = false, message = "Invalid leg data" });
                }

                // Next leg number for this strategy
                var dashboard = dataAccess.GetStrategies();
                var existing = dashboard.Legs.Where(l => l.StrategyId == model.StrategyId).ToList();
                model.LegNo = existing.Count == 0 ? 1 : existing.Max(l => l.LegNo) + 1;

                dataAccess.InsertStrategyLeg(model);

                // Strategy changed — refresh the AI comment
                await RegenerateAiRemarksAsync(model.StrategyId);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetRemarks(int strategyId)
        {
            try
            {
                return Json(new { remarks = dataAccess.GetStrategyRemarks(strategyId) });
            }
            catch (Exception ex)
            {
                return Json(new { remarks = "", error = ex.Message });
            }
        }

        // Hard delete — removes the leg from the strategy entirely (Orders page "Remove").
        // Distinct from ExitLeg, which closes the position at a price via usp_ExitPosition.
        [HttpPost]
        public async Task<IActionResult> RemoveLeg(int legId)
        {
            try
            {
                // Capture the owning strategy before the leg disappears
                int strategyId = 0;
                try
                {
                    var dashboard = dataAccess.GetStrategies();
                    strategyId = dashboard.Legs.FirstOrDefault(l => l.LegId == legId)?.StrategyId ?? 0;
                }
                catch
                {
                    // lookup failure shouldn't block the removal itself
                }

                dataAccess.DeleteStrategyLeg(legId);

                // Strategy changed — refresh the AI comment
                if (strategyId > 0)
                {
                    await RegenerateAiRemarksAsync(strategyId);
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExitLeg(int legId, decimal exitPrice)
        {
            try
            {
                if (exitPrice < 0)
                {
                    return Json(new { success = false, message = "Invalid exit price" });
                }

                // Capture the owning strategy before the leg is deactivated
                int strategyId = 0;
                try
                {
                    var dashboard = dataAccess.GetStrategies();
                    strategyId = dashboard.Legs.FirstOrDefault(l => l.LegId == legId)?.StrategyId ?? 0;
                }
                catch
                {
                    // lookup failure shouldn't block the exit itself
                }

                // Records ExitPrice/ExitDate and sets isactive=0.
                // GetAll only returns active legs, so exited legs (and strategies with
                // no active legs left) disappear from every screen.
                dataAccess.ExitPosition(legId, exitPrice);

                // Strategy changed — refresh the AI comment
                if (strategyId > 0)
                {
                    await RegenerateAiRemarksAsync(strategyId);
                }

                return Json(new
                {
                    success = true
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }
    }
}
