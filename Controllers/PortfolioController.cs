using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

namespace StockWebApplications.Controllers
{
    public class PortfolioController : Controller
    {

        // string ConnectioString = "Data Source=103.21.58.192;Initial Catalog=ifutujah_paym;Integrated Security = False; User ID = puser; Password=Concept@2711;Encrypt=False";
        DataAccess dataAccess = new DataAccess();
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public JsonResult GetPortfolioData(string script_code)
        {

            if (string.IsNullOrEmpty(script_code))
            {
                return new JsonResult(null);
            }

            if (!script_code.Contains("."))
            {
                script_code = script_code + ".NS";
            }
            var ds = dataAccess.GetPortfolioStockReport(script_code);
            var stocks = new List<Models.StockRow>();
         

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                stocks = ds.Tables[0].AsEnumerable()
          .Select(row => new StockRow
          {
              SNo = row.Field<Int64>("SNo"),

              Name = row.Field<string>("Name"),
              Qty = row.Field<int>("shares"),
              Buy = row.Field<decimal>("buyPrice"),
              Sell = row.Field<decimal>("SellPrice"),
              TotalPL = row.Field<decimal>("netProfit"),
              Duration = row.Field<string>("Duration"),
              Year = row.Field<string>("FyYear"),
              perShare = row.Field<decimal>("perShare"),
              Date = row.Field<string>("SoldOn")
          })
          .ToList();
            }

            var dividends = new List<DividendRow>();

            if (ds != null && ds.Tables[1].Rows.Count > 0)
            {

                dividends = ds.Tables[1].AsEnumerable()
          .Select(row => new DividendRow
          {
              Amount = row.Field<decimal>("Amount"),
              Date = row.Field<string>("Date")
          })
          .ToList();
            }


            return Json(new { stocks, dividends });
        }

        [HttpPost]
        public JsonResult AddDividend(string script_code, string date, decimal amount)
        {
            if (!script_code.EndsWith(".NS") || !script_code.EndsWith(".BO"))
            {
                script_code = script_code + ".NS";
            }
            dataAccess.AddDividend(script_code, date, amount);
            return Json(new { success = true });
        }

        [HttpGet]
        public JsonResult GetDividends(string script_code)
        {
            if (!script_code.EndsWith(".NS") || !script_code.EndsWith(".BO"))
            {
                script_code = script_code + ".NS";
            }
            var list = new List<DividendRow>();
            DataSet ds = dataAccess.GetDividends(script_code);

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                list = ds.Tables[0].AsEnumerable()
          .Select(row => new DividendRow
          {
              Amount = row.Field<decimal>("Amount"),
              Date = row.Field<string>("Date")
          })
          .ToList();
            }


            return Json(list);
        }



    }
}
