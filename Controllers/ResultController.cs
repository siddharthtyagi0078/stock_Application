using Microsoft.AspNetCore.Mvc;

namespace StockWebApplications.Controllers
{
    public class ResultController : Controller
    {
        DataAccess dataAccess = new DataAccess();
        private IWebHostEnvironment Environment;

        public ResultController(IWebHostEnvironment _environment)
        {
            // _logger = logger;
            Environment = _environment;
        }


        public  IActionResult Index()
        {
            dataAccess.GetResultDatesAsync(this.Environment.WebRootPath);
            if (DateTime.Now.Day%3==0)
            {
                dataAccess.GetSharedetailsWeelyAsync();
            }

            return View();
        }
    }
}
