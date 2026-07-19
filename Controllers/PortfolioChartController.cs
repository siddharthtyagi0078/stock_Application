using System.Data;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;

namespace StockWebApplications.Controllers
{
    public class PortfolioChartController : Controller
    {
        public IActionResult Index()
        {

            return View();
        }

        [HttpPost]
        public JsonResult Index(string year)
        {

            DataAccess dataAccess = new DataAccess();
            List<PortfolioChart> objResponse = new List<PortfolioChart>();
            DataSet lststockModel = dataAccess.GetPortfolioCart(year);

            foreach (DataRow stock in lststockModel.Tables[0].Rows)
            {

                PortfolioChart obj = new PortfolioChart();
                obj.units = stock["units"].ToString();
                obj.date = stock["date"].ToString();
                //  obj.date = "1506882600000";

                objResponse.Add(obj);
                //  break;
            }
            return Json(objResponse);
        }
    }
}
