using Microsoft.AspNetCore.Identity;

using RestSharp;
using StockWebApplications.Models;
using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web.Razor.Parser.SyntaxTree;
using System.Xml;
using Microsoft.AspNetCore.Mvc;

namespace StockWebApplications.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        DataAccess dataAccess = new DataAccess();

        private IWebHostEnvironment Environment;
        private readonly AngelOneClient _angel;
        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment _environment, AngelOneClient angel)
        {
            _logger = logger;
            Environment = _environment;
            _angel = angel;
        }
        
        public ActionResult Index(int val=0)
        {
            if (val==1)
            {
                TempData["IsValidUser"] = "Success";
            }
            if (TempData.Count != 0 && TempData["IsValidUser"].ToString() == "Success")
            {
                TempData["IsValidUser"] = "Success";
                return View();
            }
            else
            {
                return View("../Login/Index");
            }

        }

        private StrategyDashboardVM GetStrategies()
        {

           return dataAccess.GetStrategies();
                
        }
        public ActionResult GetStockDetails()
        {
            string vix_val = "";
          //  string indicesval = "";
            var Indices_val=new List<StockModel>();
            var vix = "";
            string mood = dataAccess.MoodIndex();
            decimal profitval = 0;
            decimal dividend = 0;
            string holiday = "";

            int count = 1;
            JsonResult result = null;
            List<StockList> lststockModel = dataAccess.GetStockDetails(out profitval,out dividend,out holiday);
            CultureInfo hindi = new CultureInfo("hi-IN");
            foreach (StockList stock in lststockModel)
            {
                // "block;";
                var display = stock.compact_view == "0" ? "block;" : "none;";
             //   stock.indicesText = indicesval;
                stock.latestValue = Math.Round(Convert.ToDecimal(stock.stockModel.close) * Convert.ToDecimal(stock.shares), 2);
                stock.daysGain = Math.Round(Convert.ToDecimal(stock.stockModel.change) * Convert.ToDecimal(stock.shares), 2);
                stock.overAllGain = Math.Round(Convert.ToDecimal(stock.stockModel.close) - Convert.ToDecimal(stock.inv_Price), 2) * Convert.ToDecimal(stock.shares);
                stock.diff = Math.Round(Convert.ToDecimal(stock.stockModel.close) - Convert.ToDecimal(stock.inv_Price), 2);

                var percentval = Convert.ToDecimal(stock.overAllGain) / (Convert.ToDecimal(stock.inv_Price) * (stock.shares));
                percentval = Math.Round(percentval * 100, 2);
                var result_val = string.IsNullOrEmpty(stock.r_notes) ? "" : "<div  style='color:orange;font-size: 22px;'>" + stock.r_notes + "</div>";

                var diff_val = stock.diff > 0 ? " <span style='color:green' >diff:+" + string.Format(hindi, "{0:c}", stock.diff) + " </span>" : " <span style='color:red'>diff:" + string.Format(hindi, "{0:c}", stock.diff) + "</span>";
                var percent_val = percentval > 0 ? "<span style='color:green'>" + "(" + percentval.ToString() + "%)" + "  <img src=\"/images/tick1.gif\" height=\"22px\" /></span>" : "<span style='color:red'>" + "(" + percentval.ToString() + "%)" + "</span>";
                //   var index_star = string.IsNullOrEmpty(stock.index) ? "" : "<img src=\"/images/star.png\" style=\"height:30px;margin-bottom:9px;\" />";
                int counts = 0;
                var vol = dataAccess.FormatNumber(stock.stockModel.volume, out counts);
                string HTMLFire = "";
                for (int i = 0; i < counts; i++)
                {
                    HTMLFire += " <img src=\"/images/fire.gif\" style=\"height:30px;margin-bottom:1px;\" />";
                }
                stock.changestr = stock.stockModel.change > 0 ? "<span style='color:green'>LTP: " + string.Format(hindi, "{0:c}", stock.stockModel.close) + " [" + stock.stockModel.change.ToString() + " (" + Math.Round(stock.stockModel.changepercent, 2) + "%)" + "]</span>" : "<span style='color:red'>LTP: " + string.Format(hindi, "{0:c}", stock.stockModel.close) + " [" + stock.stockModel.change.ToString() + " (" + Math.Round(stock.stockModel.changepercent, 2) + "%)" + "]";

                stock.changestr += " <div style='display:"+ display +"'> <div> <span     style='color:green;font-size: 20px;'> Todays: <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.low + "</span><meter style='height:23px;width:170px;margin-left:5px;' min=" + stock.stockModel.low + " value=" + stock.stockModel.adjustedClose + " max=" + stock.stockModel.high + "></meter> <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.high + "</span></div>"
           + "<div id='toggleg2" + count.ToString() + "'> <div style='color:green;font-size: 20px;'> Volume : <span style='color:darkred;font-size: 18px;'>" + vol + HTMLFire + "</span></div>"
                      + "<div> <span     style='color:green;font-size: 20px;'> 52wk: <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.FiftyTwoWeekLow + "</span><meter style='height:23px;width:170px;margin-left:5px;' min=" + stock.stockModel.FiftyTwoWeekLow + " value=" + stock.stockModel.adjustedClose + " max=" + stock.stockModel.FiftyTwoWeekHigh + "></meter> <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.FiftyTwoWeekHigh + "</span></div>"
                + "</div> </div>";


                stock.Company_name = stock.stockModel.companyName;
                stock.stockModel.companyName = "<a style = 'color:black;' href =\"#\" onclick = \"return Sellshare(" + stock.id.ToString() + ");\" > " + stock.stockModel.companyName + " </a>" +

                    "<div style='display:"+ display + "' ><div>[<span style='color:blue'>QTY: " + stock.shares.ToString() + " @ " + stock.inv_Price + "</span>][" + diff_val + "]</div>"
                    + "</span><div id='toggleg1"+count.ToString()+"'> <div     style='color:blue;font-size: 20px;'>Since:" + stock.Duration + "</div>"
                    + "<span style='color:darkred;'>["+ (stock.PE !=""? "PE : " + stock.PE+" | ":"") +  (stock.ROE !=""? "ROE: " + stock.ROE+"% | ":"") +  (stock.FV !=""? "FV: " + "₹" + stock.FV  :"") + "  ]</span>"
                    + result_val+"</div></div>";
                var todays_gain = stock.daysGain > 0 ? "<span style='color:green'>Today's Profit: " + string.Format(hindi, "{0:c}", stock.daysGain) + "</span>" : "<span style='color:red'>Today's Loss: " + string.Format(hindi, "{0:c}", stock.daysGain) + "</span>";
                stock.overAllGain_str = stock.overAllGain > 0 ? "<a href =\"#\" onclick = \"return CompactView(" + stock.id.ToString() + ",1);\"> <span style = 'color:green;font-size: 22px;' >Overall Profit:" + string.Format(hindi, "{0:c}", stock.overAllGain) + percent_val + "</span></a><div style='display:"+ display + "'> <div>  " + todays_gain + "</div><div style = 'color:green;font-size: 22px;'>[Total Value: " + string.Format(hindi, "{0:c}", stock.latestValue) + "]</span></div></div>" : "<a href =\"#\" onclick = \"return CompactView(" + stock.id.ToString() + ",1);\"><span style ='color:red;font-size: 22px;'>Overall Loss:" + string.Format(hindi, "{0:c}", stock.overAllGain) + percent_val + "</span></a><div style='display:"+ display + "'><div>" + todays_gain + "</div><div style = 'color:red;font-size: 22px;'>[Total Value: " + string.Format(hindi, "{0:c}", stock.latestValue) + "]</span></div></div>";
                stock.MMood = mood;
                stock.VIX = vix;
                if (Indices_val.Count == 0)
                {
                    Indices_val = dataAccess.Indices();
                    stock.Indices = Indices_val;
                }
               


                stock.VIX_val = vix_val;
                stock.monthly_profit = profitval;
                stock.dividend = dividend;
                stock.holiday = holiday;

                count++;
            }
             count = 1;
            int cnt = 1;
            var lst= lststockModel.OrderByDescending(x => x.daysGain);
           
            var fresult = lststockModel.OrderByDescending(x => x.overAllGain);
            foreach (var item in lst)
            {
                var color = "green";
                if (item.daysGain<=0)
                {
                    color = "red";
                }
                var ltp = item.stockModel.change > 0 ? "<span style='color:green'>LTP: " + string.Format(hindi, "{0:c}", item.stockModel.close) + " [" + item.stockModel.change.ToString() + " (" + Math.Round(item.stockModel.changepercent, 2) + "%)" + "]</span>" : "<span style='color:red'>LTP: " + string.Format(hindi, "{0:c}", item.stockModel.close) + " [" + item.stockModel.change.ToString() 
                    + " (" + Math.Round(item.stockModel.changepercent, 2) + "%)" + "]";

                fresult.FirstOrDefault().stock_listing += "<div style='color:"+color+";font-size: 20px;'> " + cnt.ToString()+". "+ item.Company_name+"<b> [ "+ item.daysGain+"/- ] </b>"+ltp+"</div><br/>";
                cnt++;
            }
            foreach (StockList stock in fresult)
            {
                var star = string.IsNullOrEmpty(stock.index) ? "" : "<img src=\"/images/star.png\" style=\"height:20px;margin-bottom:9px;\" />";
                stock.stockModel.Sno = (count++).ToString() + star;
            }
            fresult.FirstOrDefault().todayOrders=( GetStrategies());
         //   ViewBag.TodayOrders =    // 👈 important line

            result = this.Json(new { data = JsonSerializer.Serialize(fresult) });
            return result;
        }

        public ActionResult GetFySummary()
        {
            string mood = dataAccess.MoodIndex();
            var lstresult = new List<string>();
         
            DataSet ds = new DataSet();
            for (int i = 2023; i <= DateTime.Now.Year; i++)
            {
                dataAccess.GetProfitStockDetails(i.ToString(),"", out DataTable summary);
                summary.TableName = i.ToString();
                    ds.Tables.Add(summary.Copy());
                    ds.AcceptChanges();
            }
            var initialdiv = "<div id=\"resp-table\">\r\n    <div id=\"resp-table-body\">\r\n   ";
            lstresult.Add(initialdiv+GenerateHtml(ds,"160")+ "</div></div>");

            if (lstresult.Count == 0)
            {
                return this.Json("No record found.");
            }

            var result = this.Json(new { data = JsonSerializer.Serialize(lstresult) });
            return result;
        }
        // Per-account P/L rows appended to the profit-report summary.
        // P/L is pure trade P/L (perShare * shares), one row per account, coloured
        // green when >= 0 and red when negative.
        private string BuildAccountPLRows(List<ProfitStockList> stocks)
        {
            var accounts = new[] { "Arnav-Angelone", "Sid-Connect", "Archana-Angelone" };
            CultureInfo hindi = new CultureInfo("hi-IN");
            string val = "";
            foreach (var acc in accounts)
            {
                decimal pl = stocks
                    .Where(s => string.Equals(s.Account, acc, StringComparison.OrdinalIgnoreCase))
                    .Sum(s => s.perShare * s.shares);
                var color = pl < 0 ? "red" : "green";
                val += " <div class=\"resp-table-row\"><div class='table-body-cell' style='width:420px;color:" + color + ";'> " + acc + " P/L: </div>";
                val += "<div class='table-body-cell' style='color:" + color + ";font-weight:bold;'>" + string.Format(hindi, "{0:c}", Math.Round(pl, 2)) + "/-</div>";
                val += "</div>";
            }
            return val;
        }
        private string GenerateHtml(DataSet summary,string col_width)
        {
            string val = "";
            CultureInfo hindi = new CultureInfo("hi-IN");
            var WIDTH = " style='width:"+col_width+"px;' ";
            if (summary.Tables.Count > 0)
            {
                var col_count = summary.Tables[0].Columns.Count;
                for (int i = 0; i < col_count; i++)

                {
                    switch (summary.Tables[0].Columns[i].ToString())
                    {
                        case "fy_year":
                            val += " <div class=\"resp-table-row\" style=\"background:lightslategrey;width:80%\" > <div"+ WIDTH + " class='table-body-cell'> FY Year: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "</div> ";
                            }
                            val += "</div>";
                            break;

                        case "dividend":
                            val += " <div class=\"resp-table-row\"><div class='table-body-cell'" + WIDTH + "> Dividend Yield: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "/-</div> ";
                            }
                            val += "</div>";
                            break;

                        case "total_trades":
                            val += " <div class=\"resp-table-row\"><div class='table-body-cell'"+ WIDTH + "> Total trade(s): </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "profit_trade":
                            val += " <div class=\"resp-table-row\" style=\"color:green\"><div class='table-body-cell'" + WIDTH +"> Profitable trade(s): </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "loss_trade":
                            val += " <div class=\"resp-table-row\" style=\"color:red\"><div class='table-body-cell' " + WIDTH +"> Loss trade(s): </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "success_rate":
                            val += " <div class=\"resp-table-row\"  style=color:green ><div class='table-body-cell' " + WIDTH +"> Success Rate: </div>";
                            var color = "red";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                if (Convert.ToDecimal(summary.Tables[j].Rows[0]["success_rate"]) > 0)
                                {
                                    color = "green";
                                }
                                val += "<div class='table-body-cell' style='color:'" + color + "' >" + summary.Tables[j].Rows[0]["success_rate"] + "%</div> ";
                            }
                            val += "</div>";
                            break;
                        case "hld_less1month":
                            val += " <div class=\"resp-table-row\"><div class='table-body-cell' " + WIDTH +"> Held less than a Month: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + " / " + summary.Tables[j].Rows[0]["total_trades"] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "avgqty":
                            val += " <div class=\"resp-table-row\"><div class='table-body-cell' " + WIDTH +"> Average Quantity: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "avg_profit":
                            val += " <div class=\"resp-table-row\" style=color:green><div class='table-body-cell' " + WIDTH +"> Average Profit: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>" + string.Format(hindi, "{0:c}", Math.Round(Convert.ToDecimal( summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()]), 2))  +
                                 "/- (" + summary.Tables[j].Rows[0][summary.Tables[0].Columns["profit_percent"].ToString()] + "%)</div> ";
                            }
                            val += "</div>";
                            break;
                        case "total_profit":
                            var color_val = "green";
                            
                            val += " <div class=\"resp-table-row\" style=color:black;><div class='table-body-cell' " + WIDTH +"> Profit: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                if (Math.Round(Convert.ToDecimal(summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()]), 2) < 0)
                                {
                                    color_val = "red";
                                }

                                val += "<div class='table-body-cell' style='color:"+color_val+";font-size:17px;'>" + string.Format(hindi, "{0:c}", Math.Round(Convert.ToDecimal(summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()]), 2)) + "/-</div> ";
                            }
                            val += "</div>";
                            break;
                        case "net_profit":
                             color_val = "green";
                            val += " <div class=\"resp-table-row\" style=color:black><div class='table-body-cell' " + WIDTH + ">Net Profit: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                color_val = "green";
                                if (Math.Round(Convert.ToDecimal(summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()]), 2) < 0)
                                {
                                    color_val = "red";
                                }
                                val += "<div class='table-body-cell' style='color:" + color_val + "'>" + string.Format(hindi, "{0:c}", Math.Round(Convert.ToDecimal(summary.Tables[j].Rows[0][summary.Tables[0].Columns[i].ToString()]), 2)) + "/-  (Including Dividend )</div> ";
                            }
                            val += "</div>";
                            break;
                        case "max_profit":
                            val += " <div class=\"resp-table-row\" style=color:green><div class='table-body-cell' " + WIDTH +"> Maximum Profit: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>₹" + summary.Tables[j].Rows[0]["max_profit"] + "</div> ";
                            }
                            val += "</div>";
                            break;
                        case "max_loss":
                            val += " <div class=\"resp-table-row\" style=color:red><div class='table-body-cell' " + WIDTH +"> Maximum Loss: </div>";
                            for (int j = 0; j < summary.Tables.Count; j++)
                            {
                                val += "<div class='table-body-cell'>₹" + summary.Tables[j].Rows[0]["max_loss"] + "</div> ";
                            }
                            val += "</div>";
                            break;

                    }

                }
            }
            return val;
        }

        public ActionResult GetprofitStockDetails(string jsonInput)
        {
            string mood = dataAccess.MoodIndex();

            JsonResult result = null;
           var year = JsonSerializer.Deserialize<Reports>(jsonInput);
            DataTable summary ;
            List<ProfitStockList> lststockModel = dataAccess.GetProfitStockDetails(year.year,year.Script_code,out summary);
            int count = 1;
            if (lststockModel.Count==0)
            {
                return this.Json("No record found.");
            }
            CultureInfo hindi = new CultureInfo("hi-IN");
            var profitpercent = lststockModel.Average(x => x.profitpercent);
                      //     var diff_val= stock.diff > 0 ? " <span style='color:green' >diff:+" + string.Format(hindi, "{0:c}", stock.diff) + " </span>" : " <span style='color:red'>diff:" + string.Format(hindi, "{0:c}", stock.diff) + "</span>";

            foreach (ProfitStockList stock in lststockModel)
            {
                var diff_val = stock.perShare > 0 ? "<br/> <span style='color:green' >diff:+" + string.Format(hindi, "{0:c}", stock.perShare) + " </span>" : " <br/><span style='color:red'>diff:" + string.Format(hindi, "{0:c}", stock.perShare) + "</span>";

                stock.Sno = (count++).ToString();
                var profitStr = stock.profitpercent > 0 ? " <span style='color:green' >(" +  stock.profitpercent + "%) </span>" : " <span style='color:red'>(" +  stock.profitpercent + "%)</span>";
                stock.Name = stock.Name + "[<span style='color:blue'>QTY: " + stock.shares.ToString() + "</span>] "+ profitStr
               + "<br/><span     style='color:brown;font-size: 24px;'>Holding For:" + stock.Duration + "</span>";
                stock.Price_str = string.Format(hindi, "{0:c}",Math.Round(stock.buyPrice, 2)) + " / " + string.Format(hindi, "{0:c}" ,Math.Round(stock.SellPrice, 2))+ diff_val;
                stock.netProfitPercent = Math.Round(profitpercent, 2);
                var accountStr = string.IsNullOrWhiteSpace(stock.Account)
                    ? ""
                    : "<br/><span style='color:#5b21b6;font-weight:bold;'>A/C: " + stock.Account + "</span>";
                stock.SoldOn += "<br/>" + stock.FyYear + accountStr;
            }
            var resultval = new ProfitStockListResponse();
            if (lststockModel.Count > 0) {
                decimal dividend = 0;
               decimal.TryParse( summary.Rows[0]["dividend"].ToString(),out dividend);
                lststockModel[0].dividend = dividend;
            }
            resultval.Stocklist = lststockModel;

            DataSet ds = new DataSet();
            ds.Tables.Add(summary.Copy());
            var initialdiv = "<div id=\"resp-table\">\r\n    <div id=\"resp-table-body\">\r\n   ";
            resultval.Summary=initialdiv + GenerateHtml(ds,"420") + BuildAccountPLRows(lststockModel) + "</div></div>";
            //resultval.Summary = summary;

            result = this.Json(new { data = JsonSerializer.Serialize(resultval) });
            return result;
        }
        public ActionResult GetProfitStockReport(string jsonInput)
        {
            Reports stockObj = JsonSerializer.Deserialize<Reports>(jsonInput);
            List<ProfitStockList> lststockModel = dataAccess.GetProfitStockReport(stockObj.year, stockObj.month);
            int count = 1;
            foreach (ProfitStockList stock in lststockModel)
            {
                stock.Sno = (count++).ToString();
            }
            var result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
            return result;
        }

        // Daily P/L breakdown for a given month (year/month, 1-based). Powers the
        // "P/L for [Month]" popup on the Home page.
        [HttpGet]
        public JsonResult GetMonthlyPnlBreakdown(int year, int month)
        {
            try
            {
                var rows = dataAccess.GetMonthlyPnlBreakdown(year, month);
                return Json(new { ok = true, rows });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, rows = new object[0] });
            }
        }

        // P&L calendar feed: returns per-trade sold rows within [fromDate,toDate] (yyyy-MM-dd).
        [HttpGet]
        public JsonResult GetPnlCalendar(string fromDate, string toDate)
        {
            try
            {
                if (!DateTime.TryParse(fromDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
                    !DateTime.TryParse(toDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                {
                    return Json(new { ok = false, error = "Invalid date range", rows = new object[0] });
                }
                if (to < from) (from, to) = (to, from);

                var years = new List<int>();
                for (int y = from.Year; y <= to.Year; y++) years.Add(y);

                var rows = new List<object>();
                foreach (var y in years)
                {
                    DataTable _;
                    var list = dataAccess.GetProfitStockDetails(y.ToString(), "", out _);
                    foreach (var s in list)
                    {
                        if (!DateTime.TryParse(s.SoldOn, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sold))
                            continue;
                        if (sold < from.Date || sold > to.Date) continue;
                        rows.Add(new
                        {
                            date = sold.ToString("yyyy-MM-dd"),
                            name = s.Name,
                            netProfit = s.netProfit,
                            dividend = s.dividend
                        });
                    }
                }
                return Json(new { ok = true, rows });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, rows = new object[0] });
            }
        }

        // NIFTY 1-min candles with EMA10 / EMA30 and crossover trend.
        [HttpGet]
        public JsonResult GetNiftyEma(int rows = 10)
        {
            try
            {
                var data = dataAccess.GetNiftyEmaFromDb(rows);
                return Json(new { ok = true, rows = data });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, rows = new object[0] });
            }
        }

        // Today's realized P/L from the shares table (no Yahoo).
        [HttpGet]
        public JsonResult GetTodayPL()
        {
            try
            {
                var pl = dataAccess.GetTodayPL();
                return Json(new { ok = true, pl });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, pl = 0m });
            }
        }

        // Per-trade breakdown behind the "Today's P/L" popup.
        [HttpGet]
        public JsonResult GetTodayTrades()
        {
            try
            {
                var rows = dataAccess.GetTodayTrades();
                return Json(new { ok = true, rows });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message, rows = new object[0] });
            }
        }

        [HttpPost]
        public JsonResult FIIData()
        {
            //Reports stockObj = JsonSerializer.Deserialize<Reports>("");
            var lststockModel = dataAccess.GetFIIData();
            var result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
            return result;
        }


        [HttpPost]
        public JsonResult Add(string jsonInput)
        {
            try
            {
                AddStock stockObj = JsonSerializer.Deserialize<AddStock>(jsonInput);
                dataAccess.AddStock(stockObj);
                return Json("Records added Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }
        [HttpPost]
        public JsonResult AddTrack(string jsonInput)
        {
            try
            {
                AddStocktracking stockObj = JsonSerializer.Deserialize<AddStocktracking>(jsonInput);
                dataAccess.AddStocktracking(stockObj);
                return Json("Records added Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }
        public ActionResult GetStockTrackingDetails(string val)
        {
            //GetFySummary();
            JsonResult result = null;
            List<AddStocktrackingReport> lststockModel = dataAccess.GetStocktrackingDetails(val);
            int count = 1;
            CultureInfo hindi = new CultureInfo("hi-IN");
            lststockModel = lststockModel.OrderByDescending(x => x.display).ThenBy(x => x.stockModel.changepercent).ToList();
            foreach (AddStocktrackingReport stock in lststockModel)
            {

                var display = stock.compact_view == "0" ? "block;" : "none;";
                var percent_val = stock.stockModel.change > 0 ? "<span style='color:green'> LTP: " + string.Format(hindi, "{0:c}", stock.stockModel.close) + " [" + stock.stockModel.change.ToString() + " (" + Math.Round(stock.stockModel.changepercent, 2) + "%)" + "]</span>" : "<span style='color:red'> LTP: " + string.Format(hindi, "{0:c}", stock.stockModel.close) + " [" + stock.stockModel.change.ToString() + " (" + Math.Round(stock.stockModel.changepercent, 2) + "%)" + "]";
                //  percent_val+=  "<meter style='height:23px;margin-left:5px;' min=" + stock.stockModel.low + " value=" + stock.stockModel.close + " max=" + stock.stockModel.high + "></meter>";
                var index_star = string.IsNullOrEmpty(stock.index) ? "" : "<img src=\"/images/star.png\" style=\"height:20px;margin-bottom:9px;\" />";
                stock.stockModel.changepercent = Math.Round(stock.stockModel.changepercent, 2);
                stock.Sno = (count++).ToString() + index_star;
                stock.current_Price = stock.stockModel.close;
                // var percent_val = stock.stockModel.change > 0 ? "<span style='color:green'> " + stock.stockModel.change.ToString() + " (" + stock.stockModel.changepercent.ToString() + "%)" + "<span  class='upMrktS'></span></span>" : "<span style='color:red'> " + stock.stockModel.change.ToString() + " (" + stock.stockModel.changepercent.ToString() + "%)" + "<span  class='downMrktS'></span>" + "</span>";
                var result_val = string.IsNullOrEmpty(stock.r_notes) ? "" : "<div   style='color:orange;font-size: 22px;'>" + stock.r_notes + "</div>";
                stock.days = (DateTime.Now.Date.AddDays(1) - Convert.ToDateTime(stock.Date_added)).Days.ToString();
                stock.diff = Convert.ToInt32(stock.tgt_Price - (decimal)stock.current_Price);
                var tgt_val = "<div style='color:blue;'> TGT: <span>" + stock.tgt_Price + "</span><span style='color:green;font-size: 18px;'> / Diff: " + stock.diff.ToString() + "</span></div>";
                int counts = 0;
                var vol = dataAccess.FormatNumber(stock.stockModel.volume, out counts);
                string HTMLFire = "";
                for (int i = 0; i < counts; i++)
                {
                    HTMLFire += " <img src=\"/images/fire.gif\" style=\"height:30px;margin-bottom:1px;\" />";
                }
                var sdetails = "<span style='color:darkred;margin-left:15px'>[" + (stock.PE != "" ? "PE : " + stock.PE + " | " : "") + (stock.ROE != "" ? "ROE: " + stock.ROE + "% | " : "") + (stock.FV != "" ? "FV: " + "₹" + stock.FV : "") + "  ]</span>";
                stock.Script_name = index_star+stock.stockModel.companyName + percent_val
                    + "<div style='display:"+ display +"'><div> <span     style='color:green;font-size: 20px;'> Todays: <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.low + "</span><meter style='height:23px;width:170px;margin-left:5px;' min=" + stock.stockModel.low + " value=" + stock.stockModel.adjustedClose + " max=" + stock.stockModel.high + "></meter> <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.high + "</span></div>" +
                     " <div style='color:green;font-size: 20px;'> Volume : <span style='color:darkred;font-size: 18px;'>" + vol + HTMLFire + "</span>"+ sdetails + "</div>"
                        + "<div> <span     style='color:green;font-size: 20px;'> 52wk: <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.FiftyTwoWeekLow + "</span><meter style='height:23px;width:170px;margin-left:5px;' min=" + stock.stockModel.FiftyTwoWeekLow + " value=" + stock.stockModel.adjustedClose + " max=" + stock.stockModel.FiftyTwoWeekHigh + "></meter> <span style='color:darkred;font-size: 20px;'>" + stock.stockModel.FiftyTwoWeekHigh + "</span></div>" 
                + result_val+"</div>";

                stock.tgt_date += "<div style='display:"+ display +"'><div>" + tgt_val + "<div style='color:green'>Added " + stock.days + " Days ago.</div></div>";
                //if (!string.IsNullOrEmpty(stock.tgt_achieve_date))
                //{
                //    stock.tgt_achieve_date = "Target Hit on:-" + stock.tgt_achieve_date.Replace("12:00:00 AM", "");
                //}
                //if (stock.tgt_Price > 0 && (stock.current_Price >= (float)stock.tgt_Price && string.IsNullOrEmpty(stock.tgt_achieve_date)))
                //{
                //    dataAccess.updatetrackerstock(stock.id, "Target Hit on:-" + stock.tgt_achieve_date.Replace("12:00:00 AM", ""));
                //}
                // stock.current_Price_str = stock.stockModel.close + "/<span     style='color:blue;'>" + stock.tgt_Price + "</span><br/><span     style='color:green;font-size: 18px;'>Diff: " + stock.diff.ToString() + "</span>"; ;
            }
            result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
            return result;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public ActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private string FindResultDetails(string Prefix)
        {

            XmlDocument doc = new XmlDocument();
            doc.Load(string.Concat(this.Environment.WebRootPath + @"\images", "/Result.xml"));

            var elements = doc.SelectNodes("/NewDataSet/Records").Cast<XmlElement>().ToList();

            List<StockScript> ObjList = elements.Select(d =>
                new StockScript
                {
                    Notes = d.GetAttribute("Notes"),
                    Date = d.GetAttribute("Date"),
                    Name= d.GetAttribute("Script")

                }).ToList();


            //Searching records from list using LINQ query
            var Name = (from N in ObjList
                        where N.Name.ToLower().Contains(Prefix.ToLower())
                        select new { N.Date,N.Notes });
            return Name!=null ? "Result:-" + Name.FirstOrDefault().Date + " :" + Name.FirstOrDefault().Notes:"no";
        }

        [HttpPost]
        public JsonResult RemoveTracking(string id)
        {
            try
            {
                dataAccess.updatetrackerstock(Convert.ToInt32(id), "Removed from Tracking on:-" + DateTime.Now.Date.ToShortDateString());
                //AddStocktracking stockObj = JsonSerializer.Deserialize<AddStocktracking>(id);
                //dataAccess.AddStocktracking(stockObj);
                return Json("Records added Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }
        [HttpPost]
        public JsonResult RemovePin(string id)
        {
            try
            {
                dataAccess.RemovePin(Convert.ToInt32(id));
                return Json("Records updated Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }

        [HttpPost]
        public JsonResult CompactView(string id,string tracker)
        {
            try
            {
                dataAccess.updateview(Convert.ToInt32(id),tracker);
                return Json("Records updated Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }

        [HttpPost]
        public JsonResult Sell(string id,string price)
        {
            try
            {
                dataAccess.SellStock((id),price);
                return Json("Records updated Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }
    }
}
