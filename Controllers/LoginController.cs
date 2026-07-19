using Microsoft.AspNetCore.Mvc;
using StockWebApplications.Models;

namespace StockWebApplications.Controllers
{
    public class LoginController : Controller
    {
        public IActionResult Index()
        {
           
            return View();
        }

        [HttpPost]
        public IActionResult Create(LoginModel model)
        {
            if (!string.IsNullOrEmpty(model.Email) && !string.IsNullOrEmpty(model.Email))
            {
                if (model.Email.ToLower() == "admin" && model.Password.ToLower() == "1")
                {
                    TempData["IsValidUser"] = "Success";
                    TempData.Peek("IsValidUser");
                    return RedirectToAction("Index", "Home");
                }
            }
            if (!string.IsNullOrEmpty(model.Email) && !string.IsNullOrEmpty(model.Email))
            {
                if (model.Email.ToLower() == "vijay" && model.Password.ToLower() == "1")
                {
                    TempData["IsValidUser"] = "Success";
                    TempData.Peek("IsValidUser");
                    return RedirectToAction("Index", "Tracker");
                }
            }
            return RedirectToAction("Index", "Login");
        }
    }
}
