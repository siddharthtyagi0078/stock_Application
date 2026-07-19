using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;

namespace StockWebApplications.Controllers
{
    public class AlertController : Controller
    {
        // GET: HomeController1
        public ActionResult Index()
        {
            GetStockDetails();
            return View();
        }

        // GET: HomeController1/Details/5
        public void GetStockDetails()
        {
            DataAccess dataAccess = new DataAccess();

            decimal profitval = 0;
            decimal dailyPL = 0;
            decimal TottalPl = 0;
            string holiday = "";

            List<StockList> lststockModel = dataAccess.GetStockDetails(out profitval, out profitval, out holiday);
            foreach (StockList stock in lststockModel)
            {
                stock.latestValue = Math.Round(Convert.ToDecimal(stock.stockModel.close) * Convert.ToDecimal(stock.shares), 2);
                dailyPL += Math.Round(Convert.ToDecimal(stock.stockModel.change) * Convert.ToDecimal(stock.shares), 2);
                stock.overAllGain = Math.Round(Convert.ToDecimal(stock.stockModel.close) - Convert.ToDecimal(stock.inv_Price), 2) * Convert.ToDecimal(stock.shares);
                TottalPl += stock.overAllGain;
             
            }

            dataAccess.InsertDailyPL(dailyPL, TottalPl);
        }

    }
}
