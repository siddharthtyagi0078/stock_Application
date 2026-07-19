using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace StockWebApplications.Controllers
{
    public class FIIDailyController : Controller
    {
        public IActionResult Index()
        {
          var  buy = new StringBuilder();
            var sold = new StringBuilder();
            DataAccess dataAccess = new DataAccess();
         var report=   dataAccess.brokeragereport();
          var summary =  dataAccess.GetFIIDATA_Async(out buy ,out sold);
            //get brokerage Report

            ViewBag.s = sold;
            ViewBag.b = buy; 
            ViewBag.summary = summary;
            ViewBag.report = report;
            return View();
        }
    }
}
