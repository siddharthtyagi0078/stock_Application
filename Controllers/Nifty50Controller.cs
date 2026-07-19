using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;
using System.Text.Json;

namespace StockWebApplications.Controllers
{
    public class Nifty50Controller : Controller
    {
        DataAccess dataAccess = new DataAccess();
        public IActionResult Index()
        {
            
            return View();
        }

        [HttpPost]
        public IActionResult GetNifty50()
        {
            JsonResult result = null;
            List<Nifty50> lststockModel = dataAccess.GetNifty50();
            int count = 1;
             lststockModel = lststockModel.OrderBy(x => x.stockModel.changepercent).ToList();
            foreach (Nifty50 stock in lststockModel)
            { 
                stock.stockModel.changepercent = Math.Round(stock.stockModel.changepercent, 2);
                stock.Sno = (count++).ToString();
                stock.Weight += "%";
                var percent_val = stock.stockModel.change > 0 ? " <span style='color:green'> " + stock.stockModel.change.ToString() + " (" + stock.stockModel.changepercent.ToString() + "%)" + "<span  class='upMrktS'></span></span>" : "<span style='color:red'> " + stock.stockModel.change.ToString() + " (" + stock.stockModel.changepercent.ToString() + "%)" + "<span  class='downMrktS'></span>" + "</span>";

                stock.Company_Name = stock.stockModel.companyName +percent_val+ " <br/> <span     style='color:green;font-size: 18px;'> 52wk low : " + stock.stockModel.FiftyTwoWeekLow + "/ 52wk High: " + stock.stockModel.FiftyTwoWeekHigh;
              
            }         
            result = this.Json(new { data = JsonSerializer.Serialize(lststockModel) });
            return result;
        }
    }
}
