using Microsoft.AspNetCore.Mvc;

namespace QuinielaApp.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Tournament");
            ViewBag.HideNav    = true;
            ViewBag.FullWidth  = true;
            ViewBag.HideFooter = true;
            return View();
        }
    }
}
