using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;
using System.Diagnostics;
using System.Text.Json;

namespace StockWebApplications.Controllers
{
    public class TrackerController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        DataAccess dataAccess = new DataAccess();
        public TrackerController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public JsonResult AddTrack(string jsonInput)
        {
            try
            {
                AddStocktracking stockObj = JsonSerializer.Deserialize<AddStocktracking>(jsonInput);

                dataAccess.AddStocktracking(stockObj,3);
                return Json("Records added Successfully.");
            }
            catch (Exception ex)
            {
                return Json("Records not added-" + ex.Message);
            }
        }
        public IActionResult Index()
        {
            if (TempData.Count != 0 && TempData["IsValidUser"].ToString() == "Success")
            {
                // TempData["IsValidUser"] = "Success";

            }
            else
            {
                // return View("../Login/Index");
            }
            return View();
        }

             public IActionResult GetStockTrackingDetails(string val)

        {


            JsonResult result = null;
            List<AddStocktrackingReport> lststockModel = dataAccess.GetStocktrackingDetails(val);
            int count = 1;

            foreach (AddStocktrackingReport stock in lststockModel)
            {
                stock.Sno = (count++).ToString();
                stock.current_Price = stock.stockModel.close;
                var percent_val = stock.stockModel.change > 0 ? "<span style='color:green'>" + "(" + stock.stockModel.change.ToString() + ")" + "<span  class='upMrktS'></span></span>" : "<span style='color:red'>" + "(" + stock.stockModel.change.ToString() + ")" + "<span  class='downMrktS'></span>" + "</span>";

                stock.days = (DateTime.Now.Date - Convert.ToDateTime(stock.Date_added)).Days.ToString();
                //   stock.Script_name = stock.stockModel.companyName + "<br/><span     style='color:green;font-size: 18px;'> 52wk low : " + stock.stockModel.FiftyTwoWeekLow + "/ 52wk High: " + stock.stockModel.FiftyTwoWeekHigh + "</span>";
                stock.Script_name = stock.stockModel.companyName + percent_val + "<br/><span     style='color:green;font-size: 18px;'> 52wk low : " + stock.stockModel.FiftyTwoWeekLow + "/ 52wk High: " + stock.stockModel.FiftyTwoWeekHigh + "</span>";
                stock.diff = Convert.ToInt32(stock.tgt_Price - (decimal)stock.current_Price);
                if (!string.IsNullOrEmpty(stock.tgt_achieve_date))
                {
                    stock.tgt_achieve_date = "Target Hit on:-" + stock.tgt_achieve_date.Replace("12:00:00 AM", "");
                }
                if (stock.current_Price >= (float)stock.tgt_Price)
                {
                    dataAccess.updatetrackerstock(stock.id, "Target Hit on:-" + stock.tgt_achieve_date.Replace("12:00:00 AM", ""));
                }
                stock.current_Price_str = stock.stockModel.close + "/<span     style='color:blue;'>" + stock.tgt_Price + "</span><br/><span     style='color:green;font-size: 18px;'>Diff: " + stock.diff.ToString() + "</span>"; ;
            }

            result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
            return result;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
