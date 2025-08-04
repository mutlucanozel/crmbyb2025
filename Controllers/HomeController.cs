using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using crm.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;
using crm.Models;
using System.Net.Http; // HttpClient ve ilgili sınıflar için
using System.Net.Http.Headers; // AuthenticationHeaderValue için
using System.Text; // Encoding için
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using crm.Services;
using Newtonsoft.Json.Linq;
using System.IO;


namespace crm.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {

        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }
        public IActionResult Index()
{
    try
    {
        // AdminNotification tablosundan ilk notu al
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // Eğer not yoksa, varsayılan bir mesaj atanır
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification
            {
                Notification = "Henüz bir not bulunmamaktadır."
            };
        }

        // Notu View'e gönder
        return View(adminNotification);
    }
    catch (Exception ex)
    {
        // Hata olursa log yaz ve hata sayfasına yönlendir
        Console.WriteLine("Error loading page: " + ex.Message);
        return View("Error");
    }
}

        public IActionResult Dashboard()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult About()
        {
            return View();
        }
    }
}
