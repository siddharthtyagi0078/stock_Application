using Microsoft.AspNetCore.Mvc;
using RestSharp;
using StockWebApplications.Models;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Xml;

namespace StockWebApplications.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        DataAccess dataAccess = new DataAccess();
        public ReportsController(ILogger<HomeController> logger, IWebHostEnvironment _environment)
        {
            _logger = logger; Environment = _environment;
        }

        private IWebHostEnvironment Environment;
        public IActionResult Index()
        {

            if (TempData.Count != 0 && TempData["IsValidUser"].ToString() == "Success")
            {
            }
            else
            {
            }
            return View();
        }
        //public IActionResult GetStockDetails()

        //{
        //    string vix_val = "";
        //    string indices_val = "";

        //    var vix = dataAccess.GetVIX(out vix_val, out indices_val);
        //    string mood = dataAccess.MoodIndex();
        //    decimal profitval = 0;

        //    JsonResult result = null;
        //    List<StockList> lststockModel = dataAccess.GetStockDetails(out profitval);


        //    foreach (StockList stock in lststockModel)
        //    {
        //        stock.latestValue = Math.Round(Convert.ToDecimal(stock.stockModel.close) * Convert.ToDecimal(stock.shares), 2);
        //        stock.daysGain = Math.Round(Convert.ToDecimal(stock.stockModel.change) * Convert.ToDecimal(stock.shares), 2);
        //        // stock.overAllGain = Math.Round(Convert.ToDecimal(stock.stockModel.open) - Convert.ToDecimal(stock.inv_Price) * Convert.ToDecimal(stock.shares), 2);
        //        stock.overAllGain = Math.Round(Convert.ToDecimal(stock.stockModel.close) - Convert.ToDecimal(stock.inv_Price), 2) * Convert.ToDecimal(stock.shares);
        //        //  stock.stockModel.Sno = (count++).ToString();
        //        stock.change = stock.stockModel.change;
        //        stock.Company_name = stock.stockModel.companyName;
        //        stock.stockModel.companyName = stock.stockModel.companyName + "[<span style='color:blue'>QTY: " + stock.shares.ToString() + "</span>]";
        //        stock.MMood = mood;
        //        stock.VIX = vix;
        //        // stock.Indices = dataAccess.Indices();
        //        stock.diff = Math.Round(Convert.ToDecimal(stock.stockModel.close) - Convert.ToDecimal(stock.inv_Price), 2);
        //        stock.VIX_val = vix_val;
        //        stock.monthly_profit = profitval;
        //    }
        //    int count = 1;
        //    var fresult = lststockModel.OrderByDescending(x => x.overAllGain);
        //    foreach (StockList stock in fresult)
        //    {
        //        stock.stockModel.Sno = (count++).ToString();
        //    }


        //    result = this.Json(new { data = JsonSerializer.Serialize(fresult) });
        //    return result;
        //}
        //public IActionResult GetProfitStockReport(string jsonInput)
        //{
        //    Reports stockObj = JsonSerializer.Deserialize<Reports>(jsonInput);
        //    List<ProfitStockList> lststockModel = dataAccess.GetProfitStockReport(stockObj.year, stockObj.month);
        //    int count = 1;
        //    foreach (ProfitStockList stock in lststockModel)
        //    {
        //        stock.Sno = (count++).ToString();
        //    }
        //    var result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
        //    return result;
        //}

        //[HttpPost]
        //public JsonResult Add(string jsonInput)
        //{
        //    try
        //    {
        //        AddStock stockObj = JsonSerializer.Deserialize<AddStock>(jsonInput);

        //        dataAccess.AddStock(stockObj);
        //        return Json("Records added Successfully.");
        //    }
        //    catch
        //    {
        //        return Json("Records not added,");
        //    }
        //}
        //[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        //public IActionResult Error()
        //{
        //    return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        //}

        [HttpPost]
        public JsonResult Index(string Prefix)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(string.Concat(this.Environment.WebRootPath + @"\images", "/NSE.xml"));

            var elements = doc.SelectNodes("/Records/Record/Row").Cast<XmlElement>().ToList();

            List<StockScript> ObjList = elements.Select(d =>
                new StockScript
                {
                    Code = d.GetAttribute("A"),
                    Name = d.GetAttribute("A")

                }).ToList();


            //Searching records from list using LINQ query
            var Name = (from N in ObjList
                        where N.Name.ToLower().Contains(Prefix.ToLower())
                        select new { N.Name });
            return Json(Name);
        }
    }
}
