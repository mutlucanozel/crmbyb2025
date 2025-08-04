using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO; // Dosya işlemleri (File, Directory) için
using System.Linq; // LINQ işlemleri için
using System.Net;
using System.Net.Http; // HttpClient ve ilgili sınıflar için
using System.Net.Http.Headers; // AuthenticationHeaderValue için
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using crm.Data;
using crm.Models;
using crm.Services;
using Google.Cloud.Vision.V1;
using IronOcr;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Mandrill;
using Mandrill.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json; // JSON serileştirme ve çözümleme (EF Core için)
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract; // OCR işlemleri için Tesseract kütüphanesi
using ZXing;
using ZXing;
using ZXing.Common;
using ZXing.Common;
using ZXing.Common;
using ZXing.ImageSharp;
using ZXing.ImageSharp;
using ImageSharpRect = SixLabors.ImageSharp.Rectangle;
using iTextParagraph = iTextSharp.text.Paragraph;

public class ErpController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CustomerController> _logger;
    private readonly IConfiguration _configuration;
    private readonly PdfService _pdfService;
    private readonly HttpClient _httpClient;
    private readonly WhatsAppService _whatsAppService;
    private readonly ViewRenderService _viewRenderService;

    // Dependency Injection for WhatsAppService

    private static readonly string ApiKey = Environment.GetEnvironmentVariable(
        "AIzaSyChnM_cbXZ4m1zFjGhp6WyG0QJKRfbV-wQ"
    );

    public ErpController(
        ILogger<CustomerController> logger,
        ApplicationDbContext context,
        IConfiguration configuration,
        ViewRenderService viewRenderService,
        WhatsAppService whatsAppService,
        PdfService pdfService
    )
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _pdfService = pdfService;
        _whatsAppService = whatsAppService;
        _viewRenderService = viewRenderService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View("Error!");
    }

    public IActionResult Labeldefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            AdminNotification = adminNotification,
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            PaperInfos = _context.PaperInfos.ToList(),
            DeliveryMethods = _context.DeliveryMethods.ToList(),
            OrderMethods = _context.OrderMethods.ToList(),
            MailInfos = _context.MailInfos.ToList(),
            Knifes = _context.Knifes.ToList(),
            MoldCliches = _context.MoldCliches.ToList(),
            ErpMachines = _context.ErpMachines.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateKnife(Knife knife)
    {
        if (ModelState.IsValid)
        {
            _context.Knifes.Add(knife);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Bıçak başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Bıçak ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Bıçak ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("labeldefinition", "erp");
    }

    [HttpPost]
    public IActionResult UpdateKnife(Knife knife)
    {
        if (ModelState.IsValid)
        {
            _context.Knifes.Update(knife);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteKnife(int id)
    {
        var knife = _context.Knifes.Find(id);
        if (knife != null)
        {
            _context.Knifes.Remove(knife);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetKnifesById(int id)
    {
        var knifes = _context.Knifes.Find(id);
        if (knifes == null)
        {
            return NotFound();
        }
        return Json(knifes);
    }

    [HttpPost]
    public IActionResult CreateMoldCliche(MoldCliche moldCliche)
    {
        if (ModelState.IsValid)
        {
            _context.MoldCliches.Add(moldCliche);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kalıp Klişe başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kalıp Klişe ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kalıp Klişe ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("labeldefinition", "erp");
    }

    [HttpPost]
    public IActionResult UpdateMoldCliche(MoldCliche moldCliche)
    {
        if (ModelState.IsValid)
        {
            _context.MoldCliches.Update(moldCliche);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteMoldCliche(int id)
    {
        var moldCliche = _context.MoldCliches.Find(id);
        if (moldCliche != null)
        {
            _context.MoldCliches.Remove(moldCliche);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetMoldClichesById(int id)
    {
        var moldCliche = _context.MoldCliches.Find(id);
        if (moldCliche == null)
        {
            return NotFound();
        }
        return Json(moldCliche);
    }

    [HttpPost]
    public IActionResult CreateErpmachineForm(ErpMachine erpmachine)
    {
        if (ModelState.IsValid)
        {
            _context.ErpMachines.Add(erpmachine);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Makina başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Makina ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Makina ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("labeldefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteErpMachine(int id)
    {
        var erpmachine = _context.ErpMachines.Find(id);
        if (erpmachine != null)
        {
            _context.ErpMachines.Remove(erpmachine);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateErpmachine(ErpMachine erpmachine)
    {
        if (ModelState.IsValid)
        {
            _context.ErpMachines.Update(erpmachine);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetErpMachineById(int id)
    {
        var erpmachine = _context.ErpMachines.Find(id);
        if (erpmachine == null)
        {
            return NotFound();
        }
        return Json(erpmachine);
    }

    public IActionResult GeneralDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            DeliveryMethods = _context.DeliveryMethods.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateAgency(Agency agency, IFormFile LogoFile)
    {
        ModelState.Remove("LogoFile");
        string defaultLogoPath = "/uploads/logos/default-logo.webp";

        if (LogoFile != null && LogoFile.Length > 0)
        {
            var uploadsFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "logos"
            );
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(LogoFile.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                LogoFile.CopyTo(stream);
            }

            agency.LogoPath = "/uploads/logos/" + uniqueFileName;
        }
        else
        {
            agency.LogoPath = defaultLogoPath;
        }

        if (ModelState.IsValid)
        {
            // Eğer bu ajans varsayılan seçilmişse, diğerlerini pasif yap
            if (agency.isDefault)
            {
                var otherDefaultAgencies = _context.Agencies.Where(a => a.isDefault);
                foreach (var other in otherDefaultAgencies)
                {
                    other.isDefault = false;
                }
            }

            _context.Agencies.Add(agency);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Ajans başarıyla eklendi!";
        }
        else
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));
            TempData["SwalMessage"] = "Ajans ekleme hatası: " + errorMessage;
        }

        return RedirectToAction("generaldefinition", "erp");
    }

    [HttpPost]
    public IActionResult UpdateAgency(Agency agency, IFormFile Logo)
    {
        ModelState.Remove("Logo"); // Dosya zorunlu değil

        if (ModelState.IsValid)
        {
            var existingAgency = _context.Agencies.FirstOrDefault(a => a.Id == agency.Id);
            if (existingAgency == null)
                return Json(new { success = false, message = "Ajans bulunamadı." });

            // Logo güncellemesi
            if (Logo != null && Logo.Length > 0)
            {
                var uploadsFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "logos"
                );
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(Logo.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    Logo.CopyTo(stream);
                }

                existingAgency.LogoPath = "/uploads/logos/" + uniqueFileName;
            }

            // Eğer bu ajans varsayılan seçildiyse diğer ajansların isDefault'unu false yap
            if (agency.isDefault)
            {
                var otherDefaults = _context
                    .Agencies.Where(a => a.isDefault && a.Id != agency.Id)
                    .ToList();
                foreach (var other in otherDefaults)
                {
                    other.isDefault = false;
                }
            }

            // Alanları güncelle
            existingAgency.Name = agency.Name;
            existingAgency.Address = agency.Address;
            existingAgency.isDefault = agency.isDefault;

            _context.SaveChanges();

            return Json(new { success = true });
        }

        return Json(new { success = false, message = "Geçersiz veri gönderildi." });
    }

    [HttpPost]
    public IActionResult DeleteAgency(int id)
    {
        var agency = _context.Agencies.Find(id);
        if (agency != null)
        {
            _context.Agencies.Remove(agency);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetAgencyById(int id)
    {
        var agency = _context.Agencies.Find(id);
        if (agency == null)
        {
            return NotFound();
        }
        return Json(agency);
    }

    [HttpPost]
    public IActionResult CreateWarehouse(WareHouse wareHouse)
    {
        if (ModelState.IsValid)
        {
            _context.WareHouses.Add(wareHouse);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Depo başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Depo ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Makina ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("generaldefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteWareHouse(int id)
    {
        var wareHouse = _context.WareHouses.Find(id);
        if (wareHouse != null)
        {
            _context.WareHouses.Remove(wareHouse);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateWareHouse(WareHouse wareHouse)
    {
        if (ModelState.IsValid)
        {
            _context.WareHouses.Update(wareHouse);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public IActionResult GetWareHouses()
    {
        var warehouses = _context
            .WareHouses.Select(w => new
            {
                id = w.Id,
                name = w.Name,
                row = w.Row,
                no = w.No,
            })
            .ToList();

        return Json(warehouses);
    }

    [HttpGet]
    public async Task<IActionResult> GetWareHouseById(int id)
    {
        var wareHouse = _context.WareHouses.Find(id);
        if (wareHouse == null)
        {
            return NotFound();
        }
        return Json(wareHouse);
    }

    [HttpPost]
    public IActionResult CreateShippingMethod(ShippingMethod shippingMethod)
    {
        if (ModelState.IsValid)
        {
            _context.ShippingMethods.Add(shippingMethod);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Sevkiyat şekli başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Sevkiyat şekli ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Sevkiyat şekli ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("generaldefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteShippingMethod(int id)
    {
        var shippingMethod = _context.ShippingMethods.Find(id);
        if (shippingMethod != null)
        {
            _context.ShippingMethods.Remove(shippingMethod);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateShippingMethod(ShippingMethod shippingMethod)
    {
        if (ModelState.IsValid)
        {
            _context.ShippingMethods.Update(shippingMethod);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetsShippingMethodById(int id)
    {
        var shippingMethod = _context.ShippingMethods.Find(id);
        if (shippingMethod == null)
        {
            return NotFound();
        }
        return Json(shippingMethod);
    }

    [HttpGet]
    public IActionResult GetAdhesiveInfoById(int id)
    {
        var adhesiveInfo = _context.AdhesiveInfos.Find(id);
        if (adhesiveInfo == null)
        {
            return NotFound();
        }
        return Json(adhesiveInfo);
    }

    [HttpPost]
    public IActionResult UpdateAdhesiveInfo(AdhesiveInfo adhesiveInfo)
    {
        if (ModelState.IsValid)
        {
            _context.AdhesiveInfos.Update(adhesiveInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteAdhesiveInfo(int id)
    {
        var adhesiveInfo = _context.AdhesiveInfos.Find(id);
        if (adhesiveInfo != null)
        {
            _context.AdhesiveInfos.Remove(adhesiveInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    public IActionResult PaperDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            Cores = _context.Cores.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            PaperInfos = _context.PaperInfos.ToList(),
            PaperBrands = _context.PaperBrands.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreatePaperDetail(PaperDetail paperDetail)
    {
        if (ModelState.IsValid)
        {
            _context.PaperDetails.Add(paperDetail);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kağıt detayı  başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kağıt detayı ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kağıt detayı ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("paperdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeletePaperDetail(int id)
    {
        var paperDetail = _context.PaperDetails.Find(id);
        if (paperDetail != null)
        {
            _context.PaperDetails.Remove(paperDetail);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdatePaperDetail(PaperDetail paperDetail)
    {
        if (ModelState.IsValid)
        {
            _context.PaperDetails.Update(paperDetail);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetPaperDetailById(int id)
    {
        var PaperDetail = _context.PaperDetails.Find(id);
        if (PaperDetail == null)
        {
            return NotFound();
        }
        return Json(PaperDetail);
    }

    [HttpPost]
    public IActionResult CreatePaperBrand(PaperBrand paperbrand)
    {
        if (ModelState.IsValid)
        {
            _context.PaperBrands.Add(paperbrand);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kağıt markası  başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kağıt markası ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kağıt markası ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("paperdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeletePaperBrand(int id)
    {
        var paperbrand = _context.PaperBrands.Find(id);
        if (paperbrand != null)
        {
            _context.PaperBrands.Remove(paperbrand);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdatePaperBrand(PaperBrand paperbrand)
    {
        if (ModelState.IsValid)
        {
            _context.PaperBrands.Update(paperbrand);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetPaperBrandById(int id)
    {
        var paperbrand = _context.PaperBrands.Find(id);
        if (paperbrand == null)
        {
            return NotFound();
        }
        return Json(paperbrand);
    }

    [HttpPost]
    public IActionResult CreatePaperInfo(PaperInfo paperInfo)
    {
        if (ModelState.IsValid)
        {
            _context.PaperInfos.Add(paperInfo);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kağıt Bilgisi başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kağıt Bilgisi ekleme işlemi sırasında bir hata oluştu.";
        }
        return RedirectToAction("paperdefinition", "Erp");
    }

    [HttpGet]
    public IActionResult GetPaperInfoById(int id)
    {
        var paperInfo = _context.PaperInfos.Find(id);
        if (paperInfo == null)
        {
            return NotFound();
        }
        return Json(paperInfo);
    }

    [HttpPost]
    public IActionResult UpdatePaperInfo(PaperInfo paperInfo)
    {
        if (ModelState.IsValid)
        {
            _context.PaperInfos.Update(paperInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeletePaperInfo(int id)
    {
        var paperInfo = _context.PaperInfos.Find(id);
        if (paperInfo != null)
        {
            _context.PaperInfos.Remove(paperInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult CreateAdhesiveInfo(AdhesiveInfo adhesiveInfo)
    {
        if (ModelState.IsValid)
        {
            _context.AdhesiveInfos.Add(adhesiveInfo);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kağıt Bilgisi başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kağıt Bilgisi ekleme işlemi sırasında bir hata oluştu.";
        }
        return RedirectToAction("PaperDefinition", "erp");
    }

    public IActionResult CardDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            PaperInfos = _context.PaperInfos.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            PaperBrands = _context.PaperBrands.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateCore(Core core)
    {
        if (ModelState.IsValid)
        {
            _context.Cores.Add(core);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kuka başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kuka ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kuka ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("paperdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteCore(int id)
    {
        var core = _context.Cores.Find(id);
        if (core != null)
        {
            _context.Cores.Remove(core);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateCore(Core core)
    {
        if (ModelState.IsValid)
        {
            _context.Cores.Update(core);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetCoreById(int id)
    {
        var core = _context.Cores.Find(id);
        if (core == null)
        {
            return NotFound();
        }
        return Json(core);
    }

    [HttpPost]
    public IActionResult CreateStockCard(StockCard stockCard)
    {
        if (ModelState.IsValid)
        {
            _context.StockCards.Add(stockCard);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Stok kart başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Stok kart ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Stok kart ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("carddefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteStockCard(int id)
    {
        var stockCard = _context.StockCards.Find(id);
        if (stockCard != null)
        {
            _context.StockCards.Remove(stockCard);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateStockCard(StockCard stockCard)
    {
        if (ModelState.IsValid)
        {
            _context.StockCards.Update(stockCard);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetStockCardById(int id)
    {
        var stockCard = _context.StockCards.Find(id);
        if (stockCard == null)
        {
            return NotFound();
        }
        return Json(stockCard);
    }

    public IActionResult AccountingDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            PaperBrands = _context.PaperBrands.ToList(),
            CurrencyRates = _context.CurrencyRates.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateCurrencyRate(CurrencyRate currencyRate)
    {
        ModelState.Remove("CreatedAt");
        ModelState.Remove("CreatedBy");
        if (ModelState.IsValid)
        {
            // Otomatik alanlar
            currencyRate.CreatedAt = DateTime.Now;
            currencyRate.CreatedBy = User.FindFirst("FullName")?.Value ?? "Bilinmiyor";

            _context.CurrencyRates.Add(currencyRate);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Kur bilgisi başarıyla eklendi!";
        }
        else
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kur bilgisi ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }

        return RedirectToAction("accountingdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteCurrencyRate(int id)
    {
        var currencyRate = _context.CurrencyRates.Find(id);
        if (currencyRate != null)
        {
            _context.CurrencyRates.Remove(currencyRate);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateCurrencyRate(CurrencyRate currencyRate)
    {
        ModelState.Remove("CreatedAt");
        ModelState.Remove("CreatedBy");

        if (ModelState.IsValid)
        {
            var existing = _context.CurrencyRates.FirstOrDefault(x => x.Id == currencyRate.Id);
            if (existing == null)
            {
                return Json(new { success = false, message = "Kayıt bulunamadı." });
            }

            // Güncellenebilir alanlar
            existing.CurrencyDate = currencyRate.CurrencyDate;
            existing.DollarRate = currencyRate.DollarRate;
            existing.EuroRate = currencyRate.EuroRate;

            // CreatedAt güncellenir, CreatedBy sabit kalır
            existing.CreatedAt = DateTime.Now;
            existing.CreatedBy = User.FindFirst("FullName")?.Value ?? "Bilinmiyor";

            _context.CurrencyRates.Update(existing);
            _context.SaveChanges();

            return Json(new { success = true });
        }

        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrencyRateById(int id)
    {
        var currencyRate = _context.CurrencyRates.Find(id);
        if (currencyRate == null)
        {
            return NotFound();
        }
        return Json(currencyRate);
    }

    [HttpPost]
    public IActionResult CreateAdditionalProcessing(AdditionalProcessing additionalProcessing)
    {
        ModelState.Remove("OfferAdditionalProcessings");
        if (ModelState.IsValid)
        {
            _context.AdditionalProcessings.Add(additionalProcessing);
            _context.SaveChanges();
            TempData["SwalMessage"] = "İlave İşlem başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "İlave İşlem ekleme işlemi sırasında bir hata oluştu.";
            // ModelState hatalarını almak
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            // TempData ile hataları kullanıcıya göstermek
            TempData["SwalMessage"] =
                "İlave İşlem ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    public IActionResult OrderDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            OrderMethods = _context.OrderMethods.ToList(),
            PaperBrands = _context.PaperBrands.ToList(),
            Packagings = _context.Packagings.ToList(),
            Suppliers = _context.Suppliers.ToList(),
            CurrencyRates = _context.CurrencyRates.ToList(),
            ChuckDiameters = _context.ChuckDiameters.ToList(),
            CustomerAdhesives = _context.CustomerAdhesives.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateChuckDiameter(ChuckDiameter chuckDiameter)
    {
        if (ModelState.IsValid)
        {
            _context.ChuckDiameters.Add(chuckDiameter);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Kuka çapı başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Kuka çapı ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Kuka çapı ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteChuckDiameter(int id)
    {
        var chuckdiameter = _context.ChuckDiameters.Find(id);
        if (chuckdiameter != null)
        {
            _context.ChuckDiameters.Remove(chuckdiameter);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateChuckdiameter(ChuckDiameter chuckDiameter)
    {
        if (ModelState.IsValid)
        {
            _context.ChuckDiameters.Update(chuckDiameter);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetChuckDiameter(int id)
    {
        var chuckDiameter = _context.ChuckDiameters.Find(id);
        if (chuckDiameter == null)
        {
            return NotFound();
        }
        return Json(chuckDiameter);
    }

    [HttpPost]
    public IActionResult CreateCustomerAdhesive(CustomerAdhesive customerAdhesive)
    {
        if (ModelState.IsValid)
        {
            _context.CustomerAdhesives.Add(customerAdhesive);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Müşteri yapıştırma başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Müşteri yapıştırma ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Müşteri yapıştırma ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteCustomerAdhesive(int id)
    {
        var customeradhesive = _context.CustomerAdhesives.Find(id);
        if (customeradhesive != null)
        {
            _context.CustomerAdhesives.Remove(customeradhesive);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateCustomerAdhesive(CustomerAdhesive customerAdhesive)
    {
        if (ModelState.IsValid)
        {
            _context.CustomerAdhesives.Update(customerAdhesive);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomerAdhesiveById(int id)
    {
        var customeradhesive = _context.CustomerAdhesives.Find(id);
        if (customeradhesive == null)
        {
            return NotFound();
        }
        return Json(customeradhesive);
    }

    public IActionResult ProductionDefinition()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            ProductOperations = _context.ProductOperations.ToList(),
            ChuckDiameters = _context.ChuckDiameters.ToList(),
            CustomerAdhesives = _context.CustomerAdhesives.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreatePackaging(Packaging packaging)
    {
        if (ModelState.IsValid)
        {
            _context.Packagings.Add(packaging);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Paketleme başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Paketleme ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Paketleme ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeletePackaging(int id)
    {
        var packaging = _context.Packagings.Find(id);
        if (packaging != null)
        {
            _context.Packagings.Remove(packaging);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdatePackaging(Packaging packaging)
    {
        if (ModelState.IsValid)
        {
            _context.Packagings.Update(packaging);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetPackagingById(int id)
    {
        var Packaging = _context.Packagings.Find(id);
        if (Packaging == null)
        {
            return NotFound();
        }
        return Json(Packaging);
    }

    [HttpPost]
    public IActionResult CreateOrderMethod(OrderMethod orderMethod)
    {
        if (ModelState.IsValid)
        {
            _context.OrderMethods.Add(orderMethod);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Sipariş Cinsi başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Sipariş Cinsi ekleme işlemi sırasında bir hata oluştu.";
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    [HttpPost]
    public IActionResult UpdateOrderMethod(OrderMethod orderMethod)
    {
        if (ModelState.IsValid)
        {
            _context.OrderMethods.Update(orderMethod);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteOrderMethod(int id)
    {
        var orderMethod = _context.OrderMethods.Find(id);
        if (orderMethod != null)
        {
            _context.OrderMethods.Remove(orderMethod);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpGet]
    public IActionResult GetOrderMethodById(int id)
    {
        var orderMethod = _context.OrderMethods.Find(id);
        if (orderMethod == null)
        {
            return NotFound();
        }
        return Json(orderMethod);
    }

    [HttpPost]
    public IActionResult CreateProductionOperation(ProductOperation productOperation)
    {
        if (ModelState.IsValid)
        {
            _context.ProductOperations.Add(productOperation);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Üretim Operasyon başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Üretim Operasyon ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Üretim Operasyon ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("productiondefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteProductionOperation(int id)
    {
        var productOperation = _context.ProductOperations.Find(id);
        if (productOperation != null)
        {
            _context.ProductOperations.Remove(productOperation);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult CreateDeliveryMethod(DeliveryMethod deliveryMethod)
    {
        if (ModelState.IsValid)
        {
            _context.DeliveryMethods.Add(deliveryMethod);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Teslim Şekli başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Teslim Şekli ekleme işlemi sırasında bir hata oluştu.";
        }
        return RedirectToAction("generaldefinition", "erp");
    }

    [HttpGet]
    public IActionResult GetDeliveryMethodById(int id)
    {
        var deliveryMethod = _context.DeliveryMethods.Find(id);
        if (deliveryMethod == null)
        {
            return NotFound();
        }
        return Json(deliveryMethod);
    }

    [HttpPost]
    public IActionResult UpdateDeliveryMethod(DeliveryMethod deliveryMethod)
    {
        var oldDeliveryMethod = _context
            .DeliveryMethods.AsNoTracking()
            .FirstOrDefault(d => d.Id == deliveryMethod.Id);

        if (oldDeliveryMethod == null)
        {
            return Json(new { success = false, message = "Kayıt bulunamadı." });
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.DeliveryMethods.Update(deliveryMethod);
                _context.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
            }
        }

        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteDeliveryMethod(int id)
    {
        var deliveryMethod = _context.DeliveryMethods.Find(id);

        if (deliveryMethod != null)
        {
            try
            {
                // Log ekle

                _context.DeliveryMethods.Remove(deliveryMethod);
                _context.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
            }
        }

        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateProductionOperation(ProductOperation productOperation)
    {
        if (ModelState.IsValid)
        {
            _context.ProductOperations.Update(productOperation);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetProductionOperationById(int id)
    {
        var productOperation = _context.ProductOperations.Find(id);
        if (productOperation == null)
        {
            return NotFound();
        }
        return Json(productOperation);
    }

    [HttpPost]
    public IActionResult CreateSupplier(Supplier supplier)
    {
        if (ModelState.IsValid)
        {
            _context.Suppliers.Add(supplier);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Tedarikçi başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Tedarikçi ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Tedarikçi ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("orderdefinition", "erp");
    }

    [HttpPost]
    public IActionResult DeleteSupplier(int id)
    {
        var supplier = _context.Suppliers.Find(id);
        if (supplier != null)
        {
            _context.Suppliers.Remove(supplier);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateSupplier(Supplier supplier)
    {
        if (ModelState.IsValid)
        {
            _context.Suppliers.Update(supplier);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetSupplierById(int id)
    {
        var supplier = _context.Suppliers.Find(id);
        if (supplier == null)
        {
            return NotFound();
        }
        return Json(supplier);
    }

    [HttpPost]
    public IActionResult CreateWaybill(Waybill waybill)
    {
        ModelState.Remove("Supplier");
        if (ModelState.IsValid)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            waybill.CreatedById = userId;

            _context.Waybills.Add(waybill);
            _context.SaveChanges();

            TempData["SwalMessage"] = "İrsaliye başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "İrsaliye ekleme işlemi sırasında bir hata oluştu.";
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "İrsaliye ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
        }
        return RedirectToAction("waybill", "erp");
    }

    [HttpPost]
    public IActionResult DeleteWaybill(int id)
    {
        var waybill = _context.Waybills.Find(id);
        if (waybill != null)
        {
            _context.Waybills.Remove(waybill);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateWaybill(Waybill waybill)
    {
        if (ModelState.IsValid)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            waybill.CreatedById = userId;

            _context.Waybills.Update(waybill);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpGet]
    public async Task<IActionResult> GetWaybillById(int id)
    {
        var waybill = _context.Waybills.Find(id);
        if (waybill == null)
        {
            return NotFound();
        }
        return Json(waybill);
    }

    [HttpGet]
    public IActionResult Waybill(int page = 1, int pageSize = 100)
    {
        // Admin bildirimi kontrolü
        var adminNotification = _context.AdminNotification.FirstOrDefault();
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        // Toplam veri sayısı
        int totalCount = _context.Waybills.Count();

        // Sayfalama ve ViewModel hazırlığı
        var waybillViewModels = _context
            .Waybills.OrderByDescending(w => w.Id) // Burada ID'ye göre sıralama yapılıyor
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WaybillViewModel
            {
                Id = w.Id,
                CreatedAt = w.CreatedAt,
                Description = w.Description,
                SupplierName =
                    _context
                        .Suppliers.Where(s => s.Id == w.SupplierId)
                        .Select(s => s.Name)
                        .FirstOrDefault() ?? "Bilinmiyor",

                WareHouseName =
                    _context
                        .WareHouses.Where(h => h.Id == w.WareHouseId)
                        .Select(h => h.Name)
                        .FirstOrDefault() ?? "Bilinmiyor",

                CreatedByName =
                    _context
                        .Users.Where(u => u.Id == w.CreatedById)
                        .Select(u => u.FirstName + " " + u.LastName)
                        .FirstOrDefault() ?? "Bilinmiyor",
            })
            .ToList();

        // Genel ViewModel dolduruluyor
        var viewModel = new GeneralInfoViewModel
        {
            Waybills = waybillViewModels,
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            AdminNotification = adminNotification,

            // Diğer dropdown verileri
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            PaperBrands = _context.PaperBrands.ToList(),
            Suppliers = _context.Suppliers.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

  [HttpGet]
public async Task<IActionResult> RecipeList(int? status = null)
{
    var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
    var isAdmin =
        User.IsInRole("Yönetici") ||
        User.IsInRole("GENEL MÜDÜR") ||
        User.IsInRole("Grafiker") ||
        User.IsInRole("Denetlemeci");

    var query = _context.Recipes
        .AsNoTracking()
        .Include(r => r.Customer)
        .Include(r => r.Designer)
        .Include(r => r.PaperInfo)
        .Include(r => r.AdhesiveInfo)
        .Include(r => r.RecipeAdditionalProcessings)
            .ThenInclude(rap => rap.AdditionalProcessing)
        .AsQueryable();

    if (!isAdmin && int.TryParse(userIdString, out int userId))
    {
        query = query.Where(r => r.CreatedById == userId);
    }

    if (status.HasValue && status.Value > 0)
    {
        query = query.Where(r => r.CurrentStatus == status.Value);
        ViewBag.SelectedStatus = status.Value;
    }

    Console.WriteLine("📥 Recipe query executing with filters: Admin={0}, Status={1}", isAdmin, status);

    var recipes = await query
        .OrderByDescending(r => r.Id)
        .ToListAsync();

    // Designer adları için batch çekim
    var designerIds = recipes
        .Where(r => r.DesignerId.HasValue)
        .Select(r => r.DesignerId!.Value)
        .Distinct()
        .ToList();

    var designerMap = await _context.Users
        .Where(u => designerIds.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}");

    foreach (var recipe in recipes)
    {
        if (recipe.DesignerId.HasValue &&
            designerMap.TryGetValue(recipe.DesignerId.Value, out var designerName))
        {
            recipe.DesignerFullName = designerName;
        }
    }

    // CreatedBy adları için batch çekim (zaten optimizasyon yapılmıştı)
    var creatorIds = recipes
        .Where(r => r.CreatedById.HasValue)
        .Select(r => r.CreatedById!.Value)
        .Distinct()
        .ToList();

    var createdByMap = await _context.Users
        .Where(u => creatorIds.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}");

    foreach (var recipe in recipes)
    {
        recipe.CreatedByFullName =
            recipe.CreatedById.HasValue && createdByMap.TryGetValue(recipe.CreatedById.Value, out var name)
                ? name
                : "Bilinmiyor";
    }

    // Statü sayıları (global gruplama, sayfa üstü filtre sayacı için)
    var allStatusCounts = await _context.Recipes
        .GroupBy(r => r.CurrentStatus ?? 0)
        .ToDictionaryAsync(g => g.Key, g => g.Count());

    ViewBag.StatusCounts = allStatusCounts;

    return View(recipes);
}


   [HttpGet]
public async Task<IActionResult> RecipeDetail(int id)
{
    // Ana Recipe nesnesi — tüm gerekli Include'larla (RecipeFiles hariç)
    var recipe = await _context
        .Recipes.AsNoTracking()
        .Where(r => r.Id == id)
        .Include(r => r.Customer)
        .Include(r => r.Designer)
        .Include(r => r.PaperInfo)
        .Include(r => r.AdhesiveInfo)
        .Include(r => r.CustomerAdhesive)
        .Include(r => r.Packaging)
        .Include(r => r.Knife)
        .Include(r => r.MoldCliche)
        .Include(r => r.ChuckDiameter)
        .Include(r => r.DeliveryMethod)
        .Include(r => r.Core)
        .Include(r => r.RecipeLogs)
            .ThenInclude(log => log.CreatedBy)
        .Include(r => r.RecipeMachines)
            .ThenInclude(rm => rm.Machine)
        .Include(r => r.RecipeAdditionalProcessings)
            .ThenInclude(rap => rap.AdditionalProcessing)
        .FirstOrDefaultAsync();

    if (recipe == null)
        return NotFound();

    // RecipeFiles ayrı sorgu ile yükleniyor ve sıralanıyor
    recipe.RecipeFiles = await _context.RecipeFiles
        .AsNoTracking()
        .Where(f => f.RecipeId == recipe.Id)
        .Include(f => f.CreatedBy)
        .OrderByDescending(f => f.CreatedAt)
        .ToListAsync();

    // Tüm kullanıcıları tek sorguda çek (CreatedBy ve Designer için)
    var userIds = new List<int>();
    if (recipe.CreatedById.HasValue) userIds.Add(recipe.CreatedById.Value);
    if (recipe.DesignerId.HasValue) userIds.Add(recipe.DesignerId.Value);

    var users = await _context.Users
        .AsNoTracking()
        .Where(u => userIds.Contains(u.Id))
        .Select(u => new { u.Id, u.FirstName, u.LastName })
        .ToListAsync();

    var createdByUser = users.FirstOrDefault(u => u.Id == recipe.CreatedById);
    if (createdByUser != null)
        recipe.CreatedByFullName = $"{createdByUser.FirstName} {createdByUser.LastName}";

    var designerUser = users.FirstOrDefault(u => u.Id == recipe.DesignerId);
    if (designerUser != null)
        recipe.DesignerFullName = $"{designerUser.FirstName} {designerUser.LastName}";

    return View(recipe);
}

    [HttpGet]
    public IActionResult WaybillItemByWaybillId(int id)
    {
        var waybill = _context
            .Waybills.Include(w => w.WaybillItems)
            .ThenInclude(wi => wi.StockCard)
            .Include(w => w.WaybillItems)
            .ThenInclude(wi => wi.PaperDetail)
            .FirstOrDefault(w => w.Id == id);

        if (waybill == null)
        {
            return NotFound();
        }

        ViewBag.WaybillId = id;

        var adminNotification =
            _context.AdminNotification.FirstOrDefault()
            ?? new AdminNotification { Notification = "Not yok." };

        // ✅ WaybillItems'ları ID'ye göre azalan sırada sırala
        var sortedItems = waybill.WaybillItems.OrderByDescending(wi => wi.Id).ToList();

        // ✅ LabelRoll eşlemesi (LabelRollCode & CurrentRollSituation)
        var labelRolls = _context
            .LabelRolls.Select(l => new
            {
                l.WaybillItemId,
                l.LabelRollCode,
                l.CurrentRollSituation,
            })
            .ToList();

        foreach (var item in sortedItems)
        {
            var label = labelRolls.FirstOrDefault(l => l.WaybillItemId == item.Id);
            item.LabelRollCode = label?.LabelRollCode;
            item.CurrentRollSituation = label?.CurrentRollSituation;
        }

        var viewModel = new GeneralInfoViewModel
        {
            WaybillItems = sortedItems,
            AdminNotification = adminNotification,
            Suppliers = _context.Suppliers.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            PaperDetails = _context.PaperDetails.ToList(),
            PaperInfos = _context.PaperInfos.ToList(),
            PaperBrands = _context.PaperBrands.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View("WaybillItem", viewModel);
    }

    [HttpPost]
    public IActionResult CreateWaybillItem(WaybillItem waybillItem)
    {
        try
        {
            ModelState.Remove("Waybill");
            ModelState.Remove("StockCard");
            ModelState.Remove("PaperDetail");
            ModelState.Remove("LabelRolls");

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { x.Key, x.Value.Errors })
                    .ToList();

                return Json(
                    new
                    {
                        success = false,
                        message = "Form verileri hatalı",
                        errors,
                    }
                );
            }

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
            var now = DateTime.Now;

            var lastRollCode = _context
                .LabelRolls.OrderByDescending(x => x.Id)
                .Select(x => x.LabelRollCode)
                .FirstOrDefault();

            int lastNumber = 0;
            if (!string.IsNullOrEmpty(lastRollCode) && lastRollCode.StartsWith("B"))
            {
                int.TryParse(lastRollCode.Substring(1), out lastNumber);
            }

            var waybill = _context.Waybills.FirstOrDefault(w => w.Id == waybillItem.WaybillId);
            if (waybill == null)
            {
                return Json(new { success = false, message = "İrsaliye bulunamadı." });
            }

            for (int i = 0; i < waybillItem.Quantity; i++)
            {
                var newWaybillItem = new WaybillItem
                {
                    WaybillId = waybillItem.WaybillId,
                    StockCardId = waybillItem.StockCardId,
                    Number1 = waybillItem.Number1,
                    Number2 = waybillItem.Number2,
                    PaperDetailId = waybillItem.PaperDetailId,
                    Description = waybillItem.Description,
                    CreatedById = userId,
                    CreatedAt = now,
                };

                _context.WaybillItems.Add(newWaybillItem);
                _context.SaveChanges();

                string newLabelRollCode = "B" + (lastNumber + 1 + i).ToString("D7");

                var newLabelRoll = new LabelRoll
                {
                    LabelRollCode = newLabelRollCode,
                    StockCardId = newWaybillItem.StockCardId,
                    WaybillItemId = newWaybillItem.Id,
                    CurrentWidth = newWaybillItem.Number1,
                    CurrentLenght = newWaybillItem.Number2,
                    CurrentWareHouseId = waybill.Id,
                    CurrentWareHouseRowId = 0,
                    CurrentWareHouseNo = 0,
                    CurrentRollSituation = 0,
                    CurrentDeliveryId = 0,
                    isActive = true,
                    PaperDetailId = newWaybillItem.PaperDetailId,
                };

                _context.LabelRolls.Add(newLabelRoll);
            }

            _context.SaveChanges();

            return Json(
                new { success = true, message = "Tedarik ve bobin(ler) başarıyla eklendi!" }
            );
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }

    [HttpGet]
    public IActionResult GetWaybillItemById(int id)
    {
        var item = _context.WaybillItems.FirstOrDefault(x => x.Id == id);
        if (item == null)
            return NotFound();

        return Json(item);
    }

    [HttpPost]
    public IActionResult UpdateWaybillItem(WaybillItem waybillItem)
    {
        var existing = _context.WaybillItems.FirstOrDefault(x => x.Id == waybillItem.Id);
        if (existing == null)
            return NotFound();

        existing.StockCardId = waybillItem.StockCardId;
        existing.PaperDetailId = waybillItem.PaperDetailId;
        existing.Number1 = waybillItem.Number1;
        existing.Number2 = waybillItem.Number2;
        existing.Description = waybillItem.Description;
        _context.SaveChanges();

        // LabelRoll da güncellenmeli
        var roll = _context.LabelRolls.FirstOrDefault(r => r.WaybillItemId == waybillItem.Id);
        if (roll != null)
        {
            roll.StockCardId = waybillItem.StockCardId;
            roll.CurrentWidth = waybillItem.Number1;
            roll.CurrentLenght = waybillItem.Number2;
            roll.PaperDetailId = waybillItem.PaperDetailId;
            _context.SaveChanges();
        }

        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult DeleteWaybillItem(int id)
    {
        var item = _context.WaybillItems.FirstOrDefault(x => x.Id == id);
        if (item == null)
            return Json(new { success = false, message = "Kayıt bulunamadı." });

        var roll = _context.LabelRolls.FirstOrDefault(r => r.WaybillItemId == id);

        _context.WaybillItems.Remove(item);
        if (roll != null)
            _context.LabelRolls.Remove(roll);

        _context.SaveChanges();

        return Json(new { success = true });
    }

    [HttpGet]
    public IActionResult DownloadAllLabelRollsAsPdf([FromQuery] List<int> ids)
    {
        if (ids == null || !ids.Any())
        {
            return BadRequest("Etiket verisi bulunamadı.");
        }

        var waybillItems = _context
            .WaybillItems.Where(x => ids.Contains(x.Id))
            .Include(x => x.StockCard)
            .Include(x => x.PaperDetail)
            .Include(x => x.Waybill)
            .ThenInclude(w => w.Supplier)
            .ToList();

        using (var stream = new MemoryStream())
        {
            var pageSize = new iTextSharp.text.Rectangle(283.46f, 283.46f); // 10x10 cm
            var doc = new iTextSharp.text.Document(pageSize, 10f, 10f, 10f, 10f);
            var writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            var boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
            var labelFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
            var italicFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10);

            foreach (var item in waybillItems)
            {
                doc.NewPage();

                var roll = _context.LabelRolls.FirstOrDefault(l => l.WaybillItemId == item.Id);
                string rollCode = roll?.LabelRollCode ?? "BOBİN-KODU";
                string supplierName = item.Waybill?.Supplier?.Name ?? "Tedarikçi Yok";
                string stockDesc = item.StockCard?.Name ?? "-";
                string detail = item.PaperDetail?.Name ?? "-";
                string width =
                    string.Format(CultureInfo.GetCultureInfo("de-DE"), "{0:N0}", item.Number1)
                    + " cm";
                string length =
                    string.Format(CultureInfo.GetCultureInfo("de-DE"), "{0:N0}", item.Number2)
                    + " m";

                string desc = item.Description ?? "-";
                string date = item.CreatedAt.ToString(
                    "dd MMM yyyy",
                    new System.Globalization.CultureInfo("tr-TR")
                );

                // === QR ve Kod Tablosu ===
                // === QR ve Kod Tablosu ===
                var qr = new BarcodeQRCode(rollCode, 100, 100, null);
                var qrImage = qr.GetImage();
                qrImage.ScaleToFit(100f, 100f);

                var qrTable = new PdfPTable(1);
                qrTable.TotalWidth = 283.46f;
                qrTable.LockedWidth = true;

                qrTable.AddCell(
                    new PdfPCell(qrImage)
                    {
                        Border = iTextSharp.text.Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        PaddingBottom = 3f, // ↓↓↓ daha az boşluk
                    }
                );

                qrTable.AddCell(
                    new PdfPCell(new Phrase(rollCode, boldFont))
                    {
                        Border = iTextSharp.text.Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        PaddingBottom = 2f, // ↓↓↓ daha az boşluk
                    }
                );

                doc.Add(qrTable);

                // === Bilgi Tablosu ===
                PdfPTable table = new PdfPTable(2);
                table.TotalWidth = 260f;
                table.LockedWidth = true;
                table.SpacingBefore = 2f; // ↓↓↓ daha yukarı çıksın
                table.SetWidths(new float[] { 65f, 195f });

                void AddRow(string label, string value, bool italic = false, bool bold = false)
                {
                    table.AddCell(
                        new PdfPCell(new Phrase(label + ":", italic ? italicFont : labelFont))
                        {
                            Border = iTextSharp.text.Rectangle.BOX,
                            HorizontalAlignment = Element.ALIGN_LEFT,
                            VerticalAlignment = Element.ALIGN_MIDDLE,
                            Padding = 4f,
                        }
                    );

                    var styleFont = bold ? boldFont : labelFont;

                    table.AddCell(
                        new PdfPCell(new Phrase(value, styleFont))
                        {
                            Border = iTextSharp.text.Rectangle.BOX,
                            HorizontalAlignment = Element.ALIGN_LEFT,
                            VerticalAlignment = Element.ALIGN_MIDDLE,
                            Padding = 4f,
                        }
                    );
                }

                AddRow("Tarih", date, italic: true);
                AddRow("Tedarikçi", supplierName, italic: true);
                AddRow("Tanım", stockDesc, italic: true);
                AddRow("Detay", detail, italic: true);
                AddRow("Genişlik", width, italic: true);
                AddRow("Uzunluk", length, italic: true);
                AddRow("Açıklama", desc, italic: true);

                doc.Add(table);
            }

            doc.Close();
            return File(stream.ToArray(), "application/pdf", "etiketler.pdf");
        }
    }

    [HttpGet]
    public IActionResult DownloadLabelRollAsPdf(int id)
    {
        var item = _context
            .WaybillItems.Include(x => x.StockCard)
            .Include(x => x.PaperDetail)
            .Include(x => x.Waybill)
            .ThenInclude(w => w.Supplier)
            .FirstOrDefault(x => x.Id == id);

        if (item == null)
        {
            return NotFound("Etiket bulunamadı.");
        }

        var roll = _context.LabelRolls.FirstOrDefault(l => l.WaybillItemId == item.Id);

        using (var stream = new MemoryStream())
        {
            var pageSize = new iTextSharp.text.Rectangle(283.46f, 283.46f); // 10x10 cm
            var doc = new iTextSharp.text.Document(pageSize, 10f, 10f, 10f, 10f);
            var writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            // === Türkçe karakter desteği olan font ayarı ===
            string fontPath = Path.Combine("wwwroot/fonts", "DejaVuSans.ttf"); // kendi path'ine göre ayarla
            BaseFont baseFont = BaseFont.CreateFont(
                fontPath,
                BaseFont.IDENTITY_H,
                BaseFont.EMBEDDED
            );

            var labelFont = new Font(baseFont, 10);
            var boldFont = new Font(baseFont, 12, Font.BOLD);
            var italicFont = new Font(baseFont, 10, Font.ITALIC);

            string rollCode = roll?.LabelRollCode ?? "BOBİN-KODU";
            string supplierName = item.Waybill?.Supplier?.Name ?? "Tedarikçi Yok";
            string stockDesc = item.StockCard?.Name ?? "-";
            string detail = item.PaperDetail?.Name ?? "-";
            string width =
                string.Format(CultureInfo.GetCultureInfo("de-DE"), "{0:N0}", item.Number1) + " cm";
            string length =
                string.Format(CultureInfo.GetCultureInfo("de-DE"), "{0:N0}", item.Number2) + " m";
            string desc = item.Description ?? "-";

            string date = item.CreatedAt.ToString("dd MMM yyyy", new CultureInfo("tr-TR"));

            // === QR ve Kod Tablosu ===
            // === QR ve Kod Tablosu ===
            var qr = new BarcodeQRCode(rollCode, 100, 100, null);
            var qrImage = qr.GetImage();
            qrImage.ScaleToFit(100f, 100f);

            var qrTable = new PdfPTable(1);
            qrTable.TotalWidth = 283.46f;
            qrTable.LockedWidth = true;
            qrTable.AddCell(
                new PdfPCell(qrImage)
                {
                    Border = iTextSharp.text.Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    PaddingBottom = 0f, // Boşluk bırakma
                }
            );

            // 🔥 Burada Leading (satır yüksekliği) ile aralık sıfırlanıyor
            var rollCodePhrase = new Phrase(rollCode, boldFont);
            rollCodePhrase.Leading = 10f; // 🔽 satır yüksekliğini düşürür

            qrTable.AddCell(
                new PdfPCell(rollCodePhrase)
                {
                    Border = iTextSharp.text.Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    PaddingTop = -4f, // ekstra yukarı çek
                    PaddingBottom = 1f, // bilgi tablosuna geçiş için hafif boşluk
                }
            );

            doc.Add(qrTable);

            // === Bilgi Tablosu ===
            PdfPTable table = new PdfPTable(2);
            table.TotalWidth = 260f;
            table.LockedWidth = true;
            table.SpacingBefore = 2f; // ↓↓↓ daha yukarı çıksın
            table.SetWidths(new float[] { 65f, 195f });

            void AddRow(string label, string value, bool italic = false, bool bold = false)
            {
                table.AddCell(
                    new PdfPCell(new Phrase(label + ":", italic ? italicFont : labelFont))
                    {
                        Border = iTextSharp.text.Rectangle.BOX,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        Padding = 4f,
                    }
                );

                var styleFont = bold ? boldFont : labelFont;

                table.AddCell(
                    new PdfPCell(new Phrase(value, styleFont))
                    {
                        Border = iTextSharp.text.Rectangle.BOX,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        Padding = 4f,
                    }
                );
            }

            AddRow("Tarih", date, italic: true);
            AddRow("Tedarikçi", supplierName, italic: true);
            AddRow("Tanım", stockDesc, italic: true);
            AddRow("Detay", detail, italic: true);
            AddRow("Genişlik", width, italic: true);
            AddRow("Uzunluk", length, italic: true);
            AddRow("Açıklama", desc, italic: true);

            doc.Add(table);

            doc.Close();
            return File(stream.ToArray(), "application/pdf", $"etiket_{rollCode}.pdf");
        }
    }

    [HttpGet]
    public IActionResult GetLabelRoll(string qrCode)
    {
        var labelRoll = _context
            .LabelRolls.Include(l => l.WaybillItem)
            .ThenInclude(w => w.StockCard)
            .Include(l => l.WaybillItem)
            .ThenInclude(w => w.PaperDetail)
            .Include(l => l.WaybillItem)
            .ThenInclude(w => w.Waybill)
            .ThenInclude(wb => wb.Supplier)
            .FirstOrDefault(l => l.LabelRollCode == qrCode);

        if (labelRoll == null)
        {
            return Json(new { success = false, message = "Bobin bulunamadı." });
        }
        var warehouseName = _context
            .WareHouses.Where(w => w.Id == labelRoll.CurrentWareHouseId)
            .Select(w => w.Name)
            .FirstOrDefault();

        var dto = new LabelRollDto
        {
            LabelRollId = labelRoll.WaybillItem.Id,
            LabelRollCode = labelRoll.LabelRollCode,
            StockCardName = labelRoll.WaybillItem?.StockCard?.Name,
            PaperInfo = labelRoll.WaybillItem?.PaperDetail?.Name,
            Width = labelRoll.CurrentWidth,
            Length = labelRoll.CurrentLenght,
            GuncelDurum =
                labelRoll.CurrentRollSituation == 1 ? "Üretime Sarf" : "Depoya Adresleme Yapıldı",
            Depo = warehouseName,
            Adres =
                $"{(char)('A' + labelRoll.CurrentWareHouseRowId - 1)}{labelRoll.CurrentWareHouseNo}",
            Tedarikci = labelRoll.WaybillItem?.Waybill?.Supplier?.Name,
            TedarikTarihi = labelRoll.WaybillItem?.Waybill?.CreatedAt.ToString("dd MMM yyyy"),
            KullanilabilirlikDurumu = labelRoll.isActive ? "Kullanılabilir" : "Kullanılamaz",
        };

        return Json(new { success = true, data = dto });
    }

    [HttpPost]
    public IActionResult UpdateLabelRollAddress(string qrCode, int depoId, string sira, int no)
    {
        var labelRoll = _context.LabelRolls.FirstOrDefault(l => l.LabelRollCode == qrCode);
        if (labelRoll == null)
        {
            return Json(new { success = false, message = "Bobin bulunamadı." });
        }

        if (!int.TryParse(sira, out int parsedSira))
        {
            return Json(new { success = false, message = "Sıra bilgisi sayısal değil." });
        }

        labelRoll.CurrentWareHouseId = depoId;
        labelRoll.CurrentWareHouseRowId = parsedSira;
        labelRoll.CurrentWareHouseNo = no;

        _context.SaveChanges();

        return Json(new { success = true });
    }

    [DynamicAuthorize("SearchLabelRoll")]
    [HttpGet]
    public IActionResult SearchLabelRoll()
    {
        var adminNotification = _context.AdminNotification.FirstOrDefault();

        // AdminNotification boşsa varsayılan bir değer oluştur ve kaydet
        if (adminNotification == null)
        {
            adminNotification = new AdminNotification { Notification = "Not yok." };
            _context.AdminNotification.Add(adminNotification);
            _context.SaveChanges();
        }

        var viewModel = new GeneralInfoViewModel
        {
            PaperDetails = _context.PaperDetails.ToList(),
            WareHouses = _context.WareHouses.ToList(),
            AdminNotification = adminNotification,
            ProductOperations = _context.ProductOperations.ToList(),
            ChuckDiameters = _context.ChuckDiameters.ToList(),
            CustomerAdhesives = _context.CustomerAdhesives.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            StockCards = _context.StockCards.ToList(),
            ShippingMethods = _context.ShippingMethods.ToList(),
            Agencies = _context.Agencies.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }
}
