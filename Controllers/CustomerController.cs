using System;
using System.Collections.Generic; // List<T> ve koleksiyonlar için
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO; // Dosya işlemleri (File, Directory) için
using System.IO;
using System.Linq;
using System.Linq; // LINQ işlemleri için
using System.Net;
using System.Net.Http; // HttpClient ve ilgili sınıflar için
using System.Net.Http;
using System.Net.Http;
using System.Net.Http.Headers; // AuthenticationHeaderValue için
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks;
using crm.Data;
using crm.Models;
using crm.Services;
using Google.Cloud.Vision.V1;
using IronOcr;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.draw; // Add this for LineSeparator
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
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Tesseract; // OCR işlemleri için Tesseract kütüphanesi

public class CustomerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CustomerController> _logger;
    private readonly IConfiguration _configuration;
    private readonly PdfService _pdfService;
    private readonly HttpClient _httpClient;
    private readonly WhatsAppService _whatsAppService;
    private readonly ViewRenderService _viewRenderService;
    private readonly ApprovalFormGenerator _approvalFormGenerator;

    // Dependency Injection for WhatsAppService

    private static readonly string ApiKey = Environment.GetEnvironmentVariable(
        "AIzaSyChnM_cbXZ4m1zFjGhp6WyG0QJKRfbV-wQ"
    );

    public CustomerController(
        ILogger<CustomerController> logger,
        ApplicationDbContext context,
        IConfiguration configuration,
        ViewRenderService viewRenderService,
        WhatsAppService whatsAppService,
        ApprovalFormGenerator approvalFormGenerator,
        PdfService pdfService
    )
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _pdfService = pdfService;
        _approvalFormGenerator = approvalFormGenerator;
        _whatsAppService = whatsAppService;
        _viewRenderService = viewRenderService;
    }

    public IActionResult SendMessage()
    {
        var users = _context
            .Users.Select(u => new SelectListItem
            {
                Value = u.PhoneNumber,
                Text = $"{u.FirstName} {u.LastName} ({u.PhoneNumber})",
            })
            .ToList();

        ViewBag.Users = users;

        return View();
    } // POST: WhatsApp/SendMessage

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage(List<string> phoneNumbers, string message)
    {
        var results = new List<(string PhoneNumber, string Status)>();

        foreach (var number in phoneNumbers)
        {
            try
            {
                await _whatsAppService.SendMessageAsync(number, message);
                results.Add((number, "✅ Başarılı"));
            }
            catch (Exception ex)
            {
                results.Add((number, $"❌ Hata: {ex.Message}"));
            }
        }

        ViewBag.Results = results;

        // Kullanıcıları tekrar doldur
        var users = _context
            .Users.Select(u => new SelectListItem
            {
                Value = u.PhoneNumber,
                Text = $"{u.FirstName} {u.LastName} ({u.PhoneNumber})",
            })
            .ToList();

        ViewBag.Users = users;

        return View();
    }

    public IActionResult GetCities()
    {
        var filePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "CityDistricts.json"
        );
        var json = System.IO.File.ReadAllText(filePath);
        var data = JObject.Parse(json);
        var cities = data["cities"].Select(c => c["name"].ToString()).ToList();
        return Json(cities);
    }

    public IActionResult GetDistricts(string city)
    {
        var filePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "CityDistricts.json"
        );
        var json = System.IO.File.ReadAllText(filePath);
        var data = JObject.Parse(json);
        var cities = data["cities"].ToObject<List<City>>();

        var selectedCity = cities.FirstOrDefault(c => c.Name == city);
        if (selectedCity != null)
        {
            return Json(selectedCity.Districts);
        }

        return Json(new List<string>());
    }

    [HttpGet]
    public IActionResult GetContact(int id)
    {
        var contact = _context.Contacts.FirstOrDefault(c => c.Id == id);
        if (contact == null)
        {
            return NotFound();
        }

        var contactViewModel = new ContactViewModel
        {
            Id = contact.Id,
            CustomerId = contact.CustomerId,
            Title = contact.Title,
            FullName = contact.FullName,
            Gender = contact.Gender,
            PhoneNumber = contact.PhoneNumber,
            Email = contact.Email,
        };

        return Json(contactViewModel);
    }

    [HttpPost]
    public async Task<IActionResult> AddRecipe(Recipe model)
    {
        ModelState.Remove("Customer");
        ModelState.Remove("RecipeLogs");

        if (ModelState.IsValid)
        {
            // ✅ Eğer OfferId varsa, daha önce bu OfferId ile reçete var mı kontrol et
            if (model.OfferId.HasValue)
            {
                var existingRecipe = await _context.Recipes.FirstOrDefaultAsync(r =>
                    r.OfferId == model.OfferId.Value
                );

                if (existingRecipe != null)
                {
                    return Json(
                        new
                        {
                            success = false,
                            message = $"Bu teklife ait  {existingRecipe.RecipeCode} reçete kodlu bir reçete zaten oluşturulmuş reçete sayfasına gitmek ister misiniz ?",
                            existingRecipeId = existingRecipe.Id,
                        }
                    );
                }
            } // Aynı müşteride aynı isimde reçete var mı?
            var existingWithSameName = await _context.Recipes.AnyAsync(r =>
                r.CustomerId == model.CustomerId && r.RecipeName == model.RecipeName
            );

            if (existingWithSameName && !Request.Form.ContainsKey("ForceCreate")) // ekstra bayrak kontrolü
            {
                return Json(
                    new
                    {
                        success = false,
                        message = "Bu müşteri için aynı adda bir reçete zaten mevcut. Yine de oluşturmak ister misiniz?",
                        recipeNameExists = true,
                    }
                );
            }

            var customer = await _context.Customers.FindAsync(model.CustomerId);
            if (customer == null)
            {
                return Json(new { success = false, message = "Müşteri bulunamadı." });
            }

            var selectedProcessingIds = Request
                .Form["AdditionalProcessing"]
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList();

            var userId = GetCurrentUserId();

            var recipe = new Recipe
            {
                CustomerId = model.CustomerId,
                RecipeName = model.RecipeName,
                LocationId = model.LocationId,
                Width = model.Width,
                Height = model.Height,
                OuterDiameter = model.OuterDiameter,
                LabelPerWrap = model.LabelPerWrap,
                CustomerCode = model.CustomerCode,
                Quantity = model.Quantity,
                NoteToDesigner = model.NoteToDesigner,
                NoteForProduction = model.NoteForProduction,
                PaperTypeId = model.PaperTypeId,
                PaperAdhesionTypeId = model.PaperAdhesionTypeId,
                CustomerAdhesionTypeId = model.CustomerAdhesionTypeId,
                CoreDiameterId = model.CoreDiameterId,
                PackageTypeId = model.PackageTypeId,
                UnitId = model.UnitId,
                AdditionalProcessing = selectedProcessingIds.Any()
                    ? string.Join(",", selectedProcessingIds)
                    : null,
                RecipeAdditionalProcessings = selectedProcessingIds
                    .Select(id => new RecipeAdditionalProcessing { AdditionalProcessingId = id })
                    .ToList(),
                WindingDirectionType = model.WindingDirectionType,
                CoreLengthId = model.CoreLengthId,
                PaperDetailId = model.PaperDetailId,
                ShipmentMethodId = model.ShipmentMethodId,
                ArchiveStatus = 0,
                CreatedAt = DateTime.Now,
                CreatedById = userId,
                CurrentStatus = 1,
                OfferId = model.OfferId,
            };

            _context.Recipes.Add(recipe);
            await _context.SaveChangesAsync();

            recipe.RecipeCode = "R" + recipe.Id.ToString("D5");
            _context.Recipes.Update(recipe);
            await _context.SaveChangesAsync();

            _context.RecipeLogs.Add(
                new RecipeLog
                {
                    RecipeId = recipe.Id,
                    FieldName = "Yeni Reçete Kaydı",
                    OldValue = "-",
                    NewValue = model.OfferId.HasValue
                        ? $"{model.OfferId} Id'li tekliften oluşturuldu"
                        : "Yeni reçete kaydı yapıldı",
                    CreatedById = userId,
                    RecordDate = DateTime.Now,
                }
            );

            await _context.SaveChangesAsync();

            return Json(
                new
                {
                    success = true,
                    message = "Reçete başarıyla eklendi.",
                    recipeId = recipe.Id,
                }
            );
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return Json(
            new
            {
                success = false,
                message = "Gerekli alanları doldurunuz.",
                errors,
            }
        );
    }

    [HttpPost]
    public async Task<IActionResult> AddRecipeFile(IFormFile File, int RecipeId, string FileType)
    {
        try
        {
            if (File == null || File.Length == 0)
                return Json(new { success = false, message = "📎 Dosya seçilmedi veya boş." });

            var uploadsPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/uploads/recipe"
            );
            if (!Directory.Exists(uploadsPath))
                Directory.CreateDirectory(uploadsPath);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            var originalFileName = Path.GetFileNameWithoutExtension(File.FileName);
            var extension = Path.GetExtension(File.FileName);

            // Dosya adını normalize et
            var normalizedFileName = NormalizeFileName(originalFileName);
            var finalFileName = $"{timestamp}{normalizedFileName}{extension}";

            var filePath = Path.Combine(uploadsPath, finalFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await File.CopyToAsync(stream);
            }

            var userName = User.Identity?.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userName);
            if (user == null)
                return Json(
                    new { success = false, message = "❌ Kullanıcı oturumu tanımlı değil." }
                );

            var recipeFile = new RecipeFile
            {
                RecipeId = RecipeId,
                FileType = FileType,
                OriginalFileName = File.FileName,
                FileName = finalFileName,
                CreatedAt = DateTime.Now,
                CreatedById = user.Id,
            };

            _context.RecipeFiles.Add(recipeFile);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "🚨 Hata: " + ex.Message });
        }
    }

    private string NormalizeFileName(string fileName)
    {
        var replacements = new Dictionary<string, string>
        {
            { "ç", "c" },
            { "Ç", "c" },
            { "ğ", "g" },
            { "Ğ", "g" },
            { "ı", "i" },
            { "İ", "i" },
            { "ö", "o" },
            { "Ö", "o" },
            { "ş", "s" },
            { "Ş", "s" },
            { "ü", "u" },
            { "Ü", "u" },
        };

        foreach (var pair in replacements)
            fileName = fileName.Replace(pair.Key, pair.Value);

        fileName = fileName.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

        // Gereksiz karakterleri kaldır
        fileName = Regex.Replace(fileName, @"[^a-z0-9\-]", "");

        return fileName;
    }

    [HttpGet]
    public IActionResult DownloadApprovalForm(int id)
    {
        var recipe = _context
            .Recipes.Include(r => r.Customer)
            .Include(r => r.PaperInfo)
            .Include(r => r.AdhesiveInfo)
            .Include(r => r.ChuckDiameter)
            .Include(r => r.RecipeFiles) // Ürün görseli için şart
            .Include(r => r.RecipeAdditionalProcessings)
            .ThenInclude(p => p.AdditionalProcessing)
            .FirstOrDefault(r => r.Id == id);

        if (recipe == null)
            return NotFound();

        var file = _approvalFormGenerator.CreateApprovalFormPdf(recipe);
        var filename = $"{recipe.RecipeCode}_OnayFormu.pdf";

        return File(file, "application/pdf", filename);
    }

    [HttpGet]
    public IActionResult GetLocation(int id)
    {
        var location = _context.Locations.FirstOrDefault(c => c.Id == id);
        if (location == null)
        {
            return NotFound();
        }

        var locationViewModel = new LocationViewModel
        {
            Id = location.Id,
            CustomerId = location.CustomerId,
            Description = location.Description,
            Address = location.Address,
        };

        // Log the data
        Console.WriteLine(JsonConvert.SerializeObject(locationViewModel));

        return Json(locationViewModel);
    }

    [HttpPost]
    public IActionResult EditContact(ContactViewModel model)
    {
        if (ModelState.IsValid)
        {
            var contact = _context.Contacts.FirstOrDefault(c => c.Id == model.Id);
            if (contact == null)
            {
                return Json(new { success = false, message = "İletişim kişisi bulunamadı." });
            }

            // Eski değerleri sakla
            var oldTitle = contact.Title ?? string.Empty;
            var oldFullName = contact.FullName ?? string.Empty;
            var oldGender = contact.Gender ?? string.Empty;
            var oldPhoneNumber = contact.PhoneNumber ?? string.Empty;
            var oldEmail = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email;

            // Yeni değerlerle güncelle
            contact.Title = model.Title;
            contact.FullName = model.FullName;
            contact.Gender = model.Gender;
            contact.PhoneNumber = model.PhoneNumber;
            contact.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email;
            try
            {
                _context.SaveChanges();

                // Değişiklikleri logla (Her alanı ayrı ayrı kontrol et)
                if (oldTitle != model.Title)
                {
                    LogChange(
                        "Customers",
                        contact.CustomerId,
                        $"İletişim - {contact.Id} - Unvan",
                        oldTitle,
                        model.Title,
                        "Güncellendi"
                    );
                }

                if (oldFullName != model.FullName)
                {
                    LogChange(
                        "Customers",
                        contact.CustomerId,
                        $"İletişim - {contact.Id} - Ad Soyad",
                        oldFullName,
                        model.FullName,
                        "Güncellendi"
                    );
                }

                if (oldGender != model.Gender)
                {
                    LogChange(
                        "Customers",
                        contact.CustomerId,
                        $"İletişim - {contact.Id} - Cinsiyet",
                        oldGender,
                        model.Gender,
                        "Güncellendi"
                    );
                }

                if (oldPhoneNumber != model.PhoneNumber)
                {
                    LogChange(
                        "Customers",
                        contact.CustomerId,
                        $"İletişim - {contact.Id} - Telefon",
                        oldPhoneNumber,
                        model.PhoneNumber,
                        "Güncellendi"
                    );
                }

                if (oldEmail != model.Email)
                {
                    LogChange(
                        "Customers",
                        contact.CustomerId,
                        $"İletişim - {contact.Id} - Email",
                        oldEmail,
                        model.Email,
                        "Güncellendi"
                    );
                }

                return Json(new { success = true, message = "Güncelleme başarılı!" });
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = "Error updating contact",
                        error = ex.Message,
                    }
                );
            }
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();
        return Json(
            new
            {
                success = false,
                message = "Invalid data",
                errors,
            }
        );
    }

    [HttpPost]
    public async Task<IActionResult> DeleteRecord(int id)
    {
        var record = await _context.Records.FirstOrDefaultAsync(c => c.Id == id);

        if (record == null)
        {
            return Json(new { success = false, message = "Kayıt bulunamadı." });
        }

        try
        {
            // Kayıt siliniyor
            _context.Records.Remove(record);
            await _context.SaveChangesAsync();

            // Silme işlemini logla (Customers tablosu için)
            LogChange(
                "Customers",
                record.CustomerId, // Müşteri ID'si
                $"Kayıt - {record.Id}",
                $"Bilgi: {record.Information}{Environment.NewLine}"
                    + $"Planlama Tarihi: {record.PlannedDate:dd.MM.yyyy}{Environment.NewLine}"
                    + (
                        record.ActualDate.HasValue
                            ? $"Gerçekleşme Tarihi: {record.ActualDate:dd.MM.yyyy}"
                            : ""
                    ),
                string.Empty, // Yeni değer olmadığı için boş
                "Silindi"
            );

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Hata oluştu: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteContact(int id)
    {
        var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.Id == id);

        if (contact == null)
        {
            return Json(new { success = false, message = "İletişim kişisi bulunamadı." });
        }

        // Eski iletişim bilgilerini sakla
        var oldContactInfo =
            $"Unvan: {contact.Title ?? "N/A"}{Environment.NewLine}"
            + $"Ad Soyad: {contact.FullName ?? "N/A"}{Environment.NewLine}"
            + $"Cinsiyet: {contact.Gender ?? "N/A"}{Environment.NewLine}"
            + $"Telefon: {contact.PhoneNumber ?? "N/A"}{Environment.NewLine}"
            + $"Email: {contact.Email ?? "N/A"}";

        _context.Contacts.Remove(contact);
        await _context.SaveChangesAsync();

        // Silme işlemini tek bir log kaydı olarak yap
        LogChange(
            "Customers",
            contact.CustomerId,
            $"İletişim - {contact.Id}",
            oldContactInfo,
            string.Empty, // Yeni değer olmadığı için boş
            "Silindi"
        );

        return Json(new { success = true });
    }

    [DynamicAuthorize("PotentialCustomerList")]
    public async Task<IActionResult> PotentialCustomerList()
    {
        // Verileri çek ve ViewModel'e dönüştür
        var customers = await _context
            .Customers.Where(c => c.IsPotential == true)
            .Include(c => c.Contacts)
            .Include(c => c.Records)
            .Include(c => c.Locations)
            .OrderByDescending(o => o.Id)
            .Select(c => new CustomerViewModel
            {
                Id = c.Id,
                Sector = c.Sector,
                Name = c.Name,
                City = c.City,
                District = c.District,
                CreatedBy = c.CreatedBy,
                IsOwned = c.IsOwned,
                LastVisitActualDate = _context
                    .Records.Where(r => r.CustomerId == c.Id && r.Status == "Ziyaret")
                    .OrderByDescending(r => r.PlannedDate)
                    .Select(r => (DateTime?)r.PlannedDate)
                    .FirstOrDefault(),
                Contacts = c
                    .Contacts.Select(co => new ContactViewModel
                    {
                        CustomerId = c.Id, // Müşteri ID'si
                        Id = co.Id,
                        Title = co.Title,
                        FullName = co.FullName,
                        Gender = co.Gender,
                        PhoneNumber = co.PhoneNumber,
                        Email = co.Email,
                    })
                    .ToList(),
                Locations = c
                    .Locations.Select(lo => new LocationViewModel
                    {
                        CustomerId = c.Id, // Müşteri ID'si
                        Description = lo.Description,
                        Address = lo.Address,
                    })
                    .ToList(),
                Records = c
                    .Records.Select(r => new RecordViewModel
                    {
                        Id = r.Id,
                        ActualDate = r.ActualDate,
                        Status = r.Status, // ActualDate bilgisi dahil ediliyor
                    })
                    .ToList(),
            })
            .ToListAsync();

        // JSON formatına dönüştür ve ViewBag ile gönder
        var contactsJson = Newtonsoft.Json.JsonConvert.SerializeObject(
            customers.ToDictionary(c => c.Id, c => c.Contacts)
        );
        ViewBag.ContactsJson = contactsJson;

        // Sektör bilgilerini ViewBag ile gönder
        var sectors = await _context.Sectors.ToListAsync();
        ViewBag.Sectors = sectors;

        // View'a müşterileri gönder
        return View(customers);
    }

    [DynamicAuthorize("ListCustomer")]
    public async Task<IActionResult> ListCustomer()
    {
        if (
            User.IsInRole("Yönetici")
            || User.IsInRole("GENEL MÜDÜR")
            || User.IsInRole("Denetlemeci")
        )
        {
            var customers = await _context
                .Customers.Where(c => c.IsOwned == true)
                .Include(c => c.Contacts)
                .Include(c => c.Locations)
                .OrderByDescending(o => o.Id)
                .Select(c => new CustomerViewModel
                {
                    Id = c.Id,
                    Sector = c.Sector,
                    Name = c.Name,
                    City = c.City,
                    District = c.District,
                    CreatedBy = _context
                        .Users.Where(u => u.Id == c.CreatedById)
                        .Select(u => u.FirstName + " " + u.LastName) // ya da Name + " " + Surname
                        .FirstOrDefault(),
                    Contacts = c
                        .Contacts.Select(co => new ContactViewModel
                        {
                            CustomerId = c.Id, // Müşteri ID'si
                            Id = co.Id,
                            Title = co.Title,
                            FullName = co.FullName,
                            Gender = co.Gender,
                            PhoneNumber = co.PhoneNumber,
                            IsApproved = co.IsApproved,
                            Email = co.Email,
                        })
                        .ToList(),
                    Locations = c
                        .Locations.Select(lo => new LocationViewModel
                        {
                            CustomerId = c.Id, // Müşteri ID'si
                            Description = lo.Description,
                            Address = lo.Address,
                        })
                        .ToList(),
                })
                .ToListAsync();

            // Müşteri ID'sine göre Contacts'ı bir Dictionary olarak dönüştür
            var contactsJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                customers.ToDictionary(c => c.Id, c => c.Contacts)
            );
            var sectors = await _context
                .Sectors.OrderBy(s => s.Name) // burada alfabetik sırala
                .ToListAsync();

            ViewBag.Sectors = sectors;

            return View(customers);
        }
        else
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier); // Kullanıcı ID'sini string olarak al
            if (!int.TryParse(userIdString, out int userId)) // String'i int'e çevir
            {
                // Çeviri başarısız olursa, uygun bir hata mesajı göster veya işlemi durdur
                return View(
                    "Error",
                    new ErrorViewModel { RequestId = "User ID conversion failed." }
                );
            }

            var customers = await _context
                .Customers.Where(c => c.CreatedById == userId) // Filtre eklendi
                .Where(c => c.IsOwned == true)
                .Include(c => c.Contacts)
                .Include(c => c.Locations)
                .OrderByDescending(o => o.Id)
                .Select(c => new CustomerViewModel
                {
                    Id = c.Id,
                    Sector = c.Sector,
                    Name = c.Name,
                    City = c.City,
                    District = c.District,
                    CreatedBy = _context
                        .Users.Where(u => u.Id == c.CreatedById)
                        .Select(u => u.FirstName + " " + u.LastName) // ya da Name + " " + Surname
                        .FirstOrDefault(),
                    Contacts = c
                        .Contacts.Select(co => new ContactViewModel
                        {
                            CustomerId = c.Id, // Müşteri ID'si
                            Id = co.Id,
                            Title = co.Title,
                            FullName = co.FullName,
                            Gender = co.Gender,
                            PhoneNumber = co.PhoneNumber,
                            IsApproved = co.IsApproved,
                            Email = co.Email,
                        })
                        .ToList(),
                    Locations = c
                        .Locations.Select(lo => new LocationViewModel
                        {
                            CustomerId = c.Id, // Müşteri ID'si
                            Description = lo.Description,
                            Address = lo.Address,
                        })
                        .ToList(),
                })
                .ToListAsync();

            // Müşteri ID'sine göre Contacts'ı bir Dictionary olarak dönüştür
            var contactsJson = Newtonsoft.Json.JsonConvert.SerializeObject(
                customers.ToDictionary(c => c.Id, c => c.Contacts)
            );
            ViewBag.ContactsJson = contactsJson; // JSON olarak ViewBag ile gönder

            var sectors = await _context.Sectors.ToListAsync();
            ViewBag.Sectors = sectors ?? new List<Sector>();

            return View(customers);
        }
    }

    [HttpPost]
    public async Task<IActionResult> FastAddCustomer([FromBody] CustomerViewModel model)
    {
        // Gerekli olmayan alanlar için validasyon kaldırılıyor
        ModelState.Remove("District");
        ModelState.Remove("City");
        ModelState.Remove("Sector");
        ModelState.Remove("Locations");
        ModelState.Remove("Records");
        ModelState.Remove("Contacts");

        if (ModelState.IsValid)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var firstName = User.FindFirst("FirstName")?.Value;
            var lastName = User.FindFirst("LastName")?.Value;
            var createdBy = $"{firstName} {lastName}";

            if (userId == null)
            {
                return Json(new { success = false, message = "User is not logged in" });
            }

            // Var olan müşteriyi kontrol et ve ilk 3 harfi aynı olanları bul
            var existingCustomers = await _context
                .Customers.Where(c => c.Name.StartsWith(model.Name.Substring(0, 3)))
                .Select(c => new { c.Name, c.CreatedBy })
                .ToListAsync();

            if (existingCustomers.Any() && !(model.ForceAdd ?? false))
            {
                // Kullanıcıdan onay isteme durumu
                return Json(
                    new
                    {
                        success = false,
                        requiresConfirmation = true,
                        message = $"Sistemde aynı isimle başlayan başka firmalar mevcut: {string.Join(", ", existingCustomers.Select(c => $"{c.Name} ({c.CreatedBy})"))}. Bu firmayı yine de eklemek istiyor musunuz?",
                    }
                );
            }

            // Yeni müşteri ekleme işlemi
            var customer = new Customer
            {
                Sector = model.Sector?.ToUpper(CultureInfo.CurrentCulture),
                Name = model.Name?.ToUpper(CultureInfo.CurrentCulture),
                City = model.City,
                District = model.District,
                CreatedBy = createdBy,
                CreatedById = int.Parse(userId),
            };

            try
            {
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();

                // Log kaydı ekle
                LogChange(
                    "Customers",
                    customer.Id,
                    "Tüm alanlar",
                    "",
                    "Hızlı müşteri ekleme yapıldı",
                    "Oluşturuldu"
                );

                var newCustomer = new
                {
                    customer.Id,
                    customer.Sector,
                    customer.Name,
                    customer.City,
                    customer.District,
                    customer.CreatedBy,
                };

                return Json(new { success = true, customer = newCustomer });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return Json(
                    new
                    {
                        success = false,
                        message = "Error adding customer",
                        errors = new List<string> { ex.Message },
                    }
                );
            }
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();
        Console.WriteLine("Validation Errors: " + string.Join(", ", errors));

        return Json(
            new
            {
                success = false,
                message = "Geçersiz veri",
                errors,
            }
        );
    }

    [DynamicAuthorize("AddPotentialCustomer")]
    [HttpPost]
    public async Task<IActionResult> AddPotentialCustomer(CustomerViewModel model)
    {
        ModelState.Remove("District");
        ModelState.Remove("City");
        ModelState.Remove("Sector");
        ModelState.Remove("Locations");
        ModelState.Remove("Records");
        ModelState.Remove("Contacts");

        // ModelState geçerli mi?
        if (ModelState.IsValid)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var createdBy =
                $"{User.FindFirst("FirstName")?.Value} {User.FindFirst("LastName")?.Value}";

            if (userId == null)
            {
                return Json(new { success = false, message = "User is not logged in" });
            }

            var imageInfos = new List<ImageInfo>();

            if (model.UploadedImages != null && model.UploadedImages.Any())
            {
                var uploadPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "customer_images"
                );

                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }

                foreach (IFormFile image in model.UploadedImages)
                {
                    if (image.Length > 0)
                    {
                        var uniqueFileName =
                            Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                        var filePath = Path.Combine(uploadPath, uniqueFileName);

                        // Görseli kaydet
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }

                        // OCR ile metni çıkar (Asenkron metod çağırılıyor)
                        string ocrText = await ExtractTextFromImageAsync(filePath);

                        // Görsel bilgilerini ekle
                        imageInfos.Add(
                            new ImageInfo
                            {
                                Path = Path.Combine("/uploads/customer_images/", uniqueFileName)
                                    .Replace("\\", "/"),
                                Description = ocrText, // OCR'dan alınan metin
                            }
                        );
                    }
                }
            }

            var similarCompanies = await _context
                .Customers.Where(c => c.Name.StartsWith(model.Name.Substring(0, 3)))
                .Select(c => new { c.Name, c.CreatedBy })
                .ToListAsync();

            if (similarCompanies.Any() && !(model.ForceAdd ?? false)) // Eğer ForceAdd `true` değilse
            {
                var similarCompanyNames = string.Join(
                    ", ",
                    similarCompanies.Select(c => $"{c.Name} ({c.CreatedBy})")
                );
                return Json(
                    new
                    {
                        success = false,
                        message = $"Bu isimle başlayan mevcut firmalar: {similarCompanyNames}. Yine de eklemek istiyor musunuz?",
                    }
                );
            }

            var potentialCustomer = new Customer
            {
                Name = model.Name.ToUpper(CultureInfo.CurrentCulture),
                Images = imageInfos,
                IsPotential = true,
                IsOwned = false,
                CreatedBy = createdBy,
                CreatedById = int.Parse(userId),
            };

            try
            {
                _context.Customers.Add(potentialCustomer);
                await _context.SaveChangesAsync();
                return Json(new { success = true, customer = potentialCustomer });
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = "Error adding potential customer",
                        errors = new List<string> { ex.Message },
                    }
                );
            }
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();
        return Json(new { success = false, message = errors });
    }

    [DynamicAuthorize("AddPotentialCustomer")]
    [HttpPost]
    public async Task<IActionResult> AddImageToCustomer(
        int customerId,
        List<IFormFile> uploadedImages
    )
    {
        if (uploadedImages == null || !uploadedImages.Any())
        {
            return Json(new { success = false, message = "Yüklenecek görsel bulunamadı." });
        }

        var customer = await _context
            .Customers.Where(c => c.Id == customerId)
            .FirstOrDefaultAsync();

        if (customer == null)
        {
            return Json(new { success = false, message = "Müşteri bulunamadı." });
        }

        var imageInfos = new List<ImageInfo>();
        var uploadPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "uploads",
            "customer_images"
        );

        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        foreach (var image in uploadedImages)
        {
            if (image.Length > 0)
            {
                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(uploadPath, uniqueFileName);

                // Görseli kaydet
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                // OCR ile metni çıkar
                string ocrText = await ExtractTextFromImageAsync(filePath);

                // Görsel bilgilerini ekle
                imageInfos.Add(
                    new ImageInfo
                    {
                        Path = Path.Combine("/uploads/customer_images/", uniqueFileName)
                            .Replace("\\", "/"),
                        Description = ocrText,
                    }
                );
            }
        }

        // Yeni görselleri mevcut listeye ekle
        if (customer.Images == null)
        {
            customer.Images = new List<ImageInfo>();
        }
        customer.Images.AddRange(imageInfos);

        try
        {
            _context.Customers.Update(customer); // Güncelleme işlemi
            await _context.SaveChangesAsync();
            return Json(
                new
                {
                    success = true,
                    message = "Görseller başarıyla eklendi.",
                    images = imageInfos,
                }
            );
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Görseller eklenirken bir hata oluştu.",
                    errors = new List<string> { ex.Message },
                }
            );
        }
    }

    public static async Task<string> ExtractTextFromImageAsync(string imagePath)
    {
        try
        {
            // 1️⃣ Görsel dosyasını kontrol et
            if (!System.IO.File.Exists(imagePath))
            {
                return "Görsel dosyası bulunamadı.";
            }

            // 2️⃣ wwwroot klasöründeki JSON dosyasının yolunu ayarlayın
            string jsonFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "hidden-server-430505-u5-05ffd530c1a4.json"
            );

            // 3️⃣ GOOGLE_APPLICATION_CREDENTIALS ortam değişkenini ayarlayın
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", jsonFilePath);

            // 4️⃣ Google Cloud Vision API istemcisini oluştur
            var client = ImageAnnotatorClient.Create(); // Bu, varsayılan kimlik doğrulaması ile çalışır

            // 5️⃣ Görseli dosyadan yükle
            var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);

            // 6️⃣ OCR işlemini başlat
            var response = await client.DetectTextAsync(image);

            // 7️⃣ OCR sonucu kontrol et
            if (response.Count == 0) // Burada `response.Count` doğru bir şekilde kullanılıyor
            {
                return "Metin çıkarılamadı: OCR sonucu boş.";
            }

            // 8️⃣ Çıkarılan metni döndür
            var text = response[0].Description;
            return string.IsNullOrEmpty(text) ? "Metin çıkarılamadı." : text;
        }
        catch (Exception ex)
        {
            // Hata mesajını döndür
            return $"Metin çıkarılamadı: {ex.Message} - {ex.StackTrace}";
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteImage(int customerId, int imageIndex)
    {
        if (!User.IsInRole("Yönetici") && !User.IsInRole("GENEL MÜDÜR"))
        {
            return Forbid();
        }
        var customer = await _context.Customers.FindAsync(customerId);

        if (customer == null || customer.Images == null || customer.Images.Count <= imageIndex)
        {
            return Json(new { success = false, message = "Görsel bulunamadı." });
        }

        try
        {
            var filePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                customer.Images[imageIndex].Path.TrimStart('/')
            );

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            customer.Images.RemoveAt(imageIndex);
            _context.Customers.Update(customer);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Görsel başarıyla silindi." });
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Görsel silme sırasında hata oluştu.",
                    errors = new List<string> { ex.Message },
                }
            );
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddCustomer(CustomerViewModel model)
    {
        ModelState.Remove("Locations");
        ModelState.Remove("Records");
        ModelState.Remove("Contacts");

        if (ModelState.IsValid)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var firstName = User.FindFirst("FirstName")?.Value;
            var lastName = User.FindFirst("LastName")?.Value;
            var createdBy = $"{firstName} {lastName}";

            if (userId == null)
            {
                return Json(new { success = false, message = "User is not logged in" });
            }
            // Firmanın ilk üç harfiyle eşleşen mevcut firmaları sorgula
            var similarCompanies = await _context
                .Customers.Where(c => c.Name.StartsWith(model.Name.Substring(0, 3)))
                .Select(c => new { c.Name, c.CreatedBy })
                .ToListAsync();

            if (similarCompanies.Any() && !(model.ForceAdd ?? false)) // Eğer ForceAdd `true` değilse
            {
                var similarCompanyNames = string.Join(
                    ", ",
                    similarCompanies.Select(c => $"{c.Name} ({c.CreatedBy})")
                );
                return Json(
                    new
                    {
                        success = false,
                        message = $"Bu isimle başlayan mevcut firmalar: {similarCompanyNames}. Yine de eklemek istiyor musunuz?",
                    }
                );
            }

            // Yeni müşteri ekleme işlemi
            var customer = new Customer
            {
                Sector = model.Sector.ToUpper(CultureInfo.CurrentCulture),
                Name = model.Name.ToUpper(CultureInfo.CurrentCulture),
                City = model.City,
                District = model.District,
                CreatedBy = createdBy,
                CreatedById = int.Parse(userId),
            };

            try
            {
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();

                return Json(new { success = true, customer = customer });
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = "Error adding customer",
                        errors = new List<string> { ex.Message },
                    }
                );
            }
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();
        return Json(new { success = false, message = errors });
    }

    [HttpPost]
    public IActionResult EditLocation(LocationViewModel model)
    {
        if (ModelState.IsValid)
        {
            // Mevcut konumu bul
            var location = _context.Locations.FirstOrDefault(l => l.Id == model.Id);
            if (location == null)
            {
                return Json(new { success = false, message = "Lokasyon bulunamadı." });
            }

            // Eski değerleri sakla
            var oldAddress = location.Address ?? string.Empty;
            var oldDescription = location.Description ?? string.Empty;

            // Yeni değerlerle güncelle
            location.Address = model.Address;
            location.Description = model.Description;

            _context.SaveChanges();

            // Değişen alanları ayrı ayrı logla
            LogIfChanged("Adres", oldAddress, model.Address, location.CustomerId, location.Id);
            LogIfChanged(
                "Tanım",
                oldDescription,
                model.Description,
                location.CustomerId,
                location.Id
            );

            return Json(new { success = true });
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return Json(
            new
            {
                success = false,
                message = "Geçersiz veri",
                errors,
            }
        );
    }

    // Yalnızca değişen değerleri loglama fonksiyonu
    private void LogIfChanged(
        string fieldName,
        string oldValue,
        string newValue,
        int customerId,
        int locationId
    )
    {
        oldValue = oldValue ?? string.Empty;
        newValue = newValue ?? string.Empty;

        if (oldValue != newValue)
        {
            LogChange(
                "Customers",
                customerId,
                $"Konum - {locationId} - {fieldName}",
                oldValue,
                newValue,
                "Güncellendi"
            );
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLocation(int id)
    {
        // Mevcut konumu bul
        var location = await _context.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location == null)
        {
            return Json(new { success = false, message = "Lokasyon bulunamadı." });
        }

        _context.Locations.Remove(location);
        await _context.SaveChangesAsync();

        // Silme işlemini logla
        LogChange(
            "Customers",
            location.CustomerId, // Doğru müşteri ID'si burada kullanılır
            $"Konum - {location.Id}",
            $"Adres: {location.Address}{Environment.NewLine}Tanım: {location.Description}",
            string.Empty, // Yeni değer yok
            "Silindi"
        );

        return Json(new { success = true });
    }

    [HttpPost]
    public IActionResult SetActualDate(int id, string actualDate)
    {
        // İlgili kaydı bulun
        var record = _context.Records.Find(id);

        if (record == null)
        {
            return Json(new { success = false, message = "Kayıt bulunamadı." });
        }

        // Eski değeri sakla
        var oldActualDate = record.ActualDate;

        try
        {
            // actualDate `null` veya boş ise `ActualDate` alanını sıfırla
            if (string.IsNullOrEmpty(actualDate))
            {
                record.ActualDate = null;
            }
            else
            {
                record.ActualDate = DateTime.Parse(actualDate);
            }

            _context.SaveChanges();

            // Sadece değişiklik varsa logla
            if (oldActualDate != record.ActualDate)
            {
                LogChange(
                    "Customers",
                    record.CustomerId,
                    $"Kayıt - {record.Id} - Gerçekleşme Tarihi",
                    oldActualDate?.ToString("dd.MM.yyyy") ?? string.Empty,
                    record.ActualDate?.ToString("dd.MM.yyyy") ?? string.Empty,
                    oldActualDate == null ? "Güncellendi" : "Silindi"
                );
            }

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Hata oluştu: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> EditRecord(
        [FromForm] Record model,
        [FromForm] string recordType
    )
    {
        ModelState.Remove("RecordType");

        ModelState.Remove("Status");
        ModelState.Remove("Customer");

        if (ModelState.IsValid)
        {
            // Retrieve the existing record
            var record = await _context.Records.FindAsync(model.Id);
            if (record == null)
            {
                return Json(new { success = false, message = "Record not found." });
            }

            var oldPlannedDate = record.PlannedDate;
            var oldActualDate = record.ActualDate;
            var oldInformation = record.Information;

            // Güncelleme işlemi
            record.PlannedDate = model.PlannedDate;
            record.ActualDate = model.ActualDate;
            record.Information = model.Information;

            try
            {
                await _context.SaveChangesAsync();

                // Sadece değişen değerleri logla
                LogChangedFields(record, oldPlannedDate, oldActualDate, oldInformation, recordType);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(
                    new
                    {
                        success = false,
                        message = "An error occurred while saving changes. Please try again.",
                        exception = ex.Message,
                    }
                );
            }
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e =>
                string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage
            )
            .ToList();

        return Json(
            new
            {
                success = false,
                message = "Invalid data.",
                errors,
            }
        );
    }

    private void LogChangedFields(
        Record record,
        DateTime? oldPlannedDate,
        DateTime? oldActualDate,
        string oldInformation,
        string recordType
    )
    {
        if (oldInformation != record.Information)
        {
            LogChange(
                "Customers",
                record.CustomerId,
                $"Kayıt - {record.Id} - Açıklama",
                oldInformation ?? "N/A",
                record.Information ?? "N/A",
                "Güncellendi"
            );
        }

        if (oldPlannedDate != record.PlannedDate)
        {
            LogChange(
                "Customers",
                record.CustomerId,
                $"Kayıt - {record.Id} - Planlama Tarihi",
                oldPlannedDate?.ToString("dd.MM.yyyy") ?? "N/A",
                record.PlannedDate?.ToString("dd.MM.yyyy") ?? "N/A",
                "Güncellendi"
            );
        }

        if (oldActualDate != record.ActualDate)
        {
            LogChange(
                "Customers",
                record.CustomerId,
                $"Kayıt - {record.Id} - Gerçekleşme Tarihi",
                oldActualDate?.ToString("dd.MM.yyyy") ?? "N/A",
                record.ActualDate?.ToString("dd.MM.yyyy") ?? "N/A",
                "Güncellendi"
            );
        }
    }

    // Fetch Phone Call Record by ID
    [HttpGet]
    public async Task<IActionResult> GetRecord(int id)
    {
        var record = await _context.Records.FindAsync(id);

        if (record == null)
            return NotFound();

        return Json(
            new
            {
                id = record.Id,
                plannedDate = record.PlannedDate?.ToString("yyyy-MM-dd"), // Format for HTML5 date input
                actualDate = record.ActualDate?.ToString("yyyy-MM-dd"),
                information = record.Information,
            }
        );
    }

    [HttpGet]
    public IActionResult GetCustomerResponsibles(int customerId)
    {
        // Örnek: tüm kullanıcı listesinden getiriyorsak
        var responsibleUsers = _context
            .Users.Select(u => new
            {
                id = u.Id,
                fullName = u.FirstName + " " + u.LastName, // varsa FirstName & LastName
            })
            .ToList();

        return Json(responsibleUsers);
    }

    [HttpGet]
    public IActionResult GetCustomerResponsible(int customerId)
    {
        var customer = _context.Customers.FirstOrDefault(c => c.Id == customerId);

        if (customer == null)
            return NotFound();

        return Ok(customer.CreatedById); // ← burada sorumlu ID dönüyor mu?
    }

    [HttpGet]
    public IActionResult GetAllSalesRepresentatives()
    {
        var salesUsers = _context
            .Users.Where(u => u.UserRoles.Any(r => r.Role == "SATIŞ TEMSILCISI"))
            .Select(u => new { id = u.Id, fullName = u.FirstName + " " + u.LastName })
            .ToList();

        return Json(salesUsers);
    }

    [HttpPost]
    public IActionResult AddRecord(RecordViewModel model, string recordType)
    {
        ModelState.Remove("Status");

        // Validate if the Customer ID exists
        var customer = _context.Customers.FirstOrDefault(c => c.Id == model.CustomerId);
        if (customer == null)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Geçersiz müşteri ID.",
                    errors = new List<string> { "Müşteri bulunamadı." },
                }
            );
        }
        var oldCreatedBy = _context
            .Users.Where(u => u.Id == customer.CreatedById)
            .Select(u => u.FirstName + " " + u.LastName) // ya da Name + " " + Surname
            .FirstOrDefault();

        if (ModelState.IsValid)
        {
            // Create a new record based on the type
            var newRecord = new Record
            {
                CustomerId = model.CustomerId,
                Status = recordType,
                PlannedDate = model.PlannedDate,
                ActualDate = model.ActualDate,
                Information = model.Information,
            };

            _context.Records.Add(newRecord);

            if (recordType.ToLower() == "görev")
            {
                var responsibleUserIdString = Request.Form["customerResponsible"];
                if (
                    int.TryParse(responsibleUserIdString, out int responsibleUserId)
                    && responsibleUserId > 0
                )
                {
                    customer.CreatedById = responsibleUserId;
                    string newCreatedBy = _context
                        .Users.Where(u => u.Id == customer.CreatedById)
                        .Select(u => u.FirstName + " " + u.LastName) // ya da Name + " " + Surname
                        .FirstOrDefault();
                    // Sadece bu alanı değiştir
                    _context.Attach(customer);
                    AddLogIfChanged(
                        "CreatedBy",
                        oldCreatedBy.ToString(),
                        newCreatedBy.ToString(),
                        customer.Id
                    );
                    _context.Entry(customer).Property(x => x.CreatedById).IsModified = true;
                }
            }

            _context.SaveChanges(); // Save changes to get the generated Id

            // Log the record addition
            LogChange(
                "Customers",
                model.CustomerId,
                $"{recordType} Kaydı - {newRecord.Id}",
                "",
                $"Açıklama: {newRecord.Information}{Environment.NewLine}"
                    + $"Planlama Tarihi: {newRecord.PlannedDate:dd.MM.yyyy}{Environment.NewLine}"
                    + (
                        newRecord.ActualDate.HasValue
                            ? $"Gerçekleşme Tarihi: {newRecord.ActualDate:dd.MM.yyyy}"
                            : ""
                    ),
                "Oluşturuldu"
            );

            // Eğer recordType "ziyaret" ise e-posta gönder
            if (recordType.ToLower() == "ziyaret")
            {
                string subject = "Yeni Ziyaret Kaydı Oluşturuldu";
                string htmlContent =
                    $@"
<!DOCTYPE html>
<html lang='tr'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Yeni Ziyaret Kaydı</title>
 <style>
        body {{
            font-family: Arial, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f4f4f4;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            padding: 20px;
            border-radius: 8px;
        }}
        .header {{
            background-color: #1F253A;
            color: white;
            text-align: center;
            padding: 10px 0;
            font-size: 20px;
            border-radius: 16px 16px 0 0;
        }}
        .content p {{
            margin: 5px 30px;
            line-height: 2.1;
        }}
        .button {{
            display: block;
            width: 80%;
            margin: 20px auto;
            text-align: center;
            background-color: #1F253A;
            color: white;
            padding: 10px;
            border-radius: 10px;
            text-decoration: none;
            font-weight: bold;
        }}
        .footer {{
            background-color: #1F253A;
            color: white;
            text-align: center;
            padding: 10px;
            border-radius: 0 0 16px 16px;
            font-size: 14px;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>📅 Yeni Ziyaret Kaydı</div>
        <div class='content'>
            <p><strong>Planlama Tarihi:</strong> {newRecord.PlannedDate:dd.MM.yyyy}</p>
            {(newRecord.ActualDate.HasValue ? $"<p><strong>Gerçekleşme Tarihi:</strong> {newRecord.ActualDate:dd.MM.yyyy}</p>" : "")}
            <p><strong>Açıklama:</strong> {newRecord.Information}</p>
            <a href='{Url.Action("PotentialCustomerDetail", "Customer", new { id = customer.Id, }, Request.Scheme)}' class='button'>Detayları İncele</a>
        </div>
        <div class='footer'>
            © 2024 | BYB CRM - Tüm Hakları Saklıdır.
        </div>
    </div>
</body>
</html>";

                // Get all users with the role "Yazılım Sorumlusu"
                var yazilimSorumlusuEmails = _context
                    .Users.Where(u => u.UserRoles.Any(ur => ur.Role == "Yazilim Sorumlusu"))
                    .Select(u => u.Email)
                    .Distinct()
                    .ToList();

                try
                {
                    foreach (var email in yazilimSorumlusuEmails)
                    {
                        SendVisitRecordEmail(email, subject, htmlContent);
                    }

                    // Eğer tüm mailler başarılı bir şekilde gönderildiyse
                    return Json(new { success = true, message = "E-posta başarıyla gönderildi." });
                }
                catch (Exception ex)
                {
                    // Hata oluştuysa kullanıcıya bildir
                    return Json(
                        new
                        {
                            success = false,
                            message = "E-posta gönderimi sırasında bir hata oluştu.",
                            error = ex.Message,
                        }
                    );
                }
            }

            return Json(new { success = true });
        }

        // Collect and return any validation errors
        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e =>
                string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage
            )
            .ToList();

        return Json(
            new
            {
                success = false,
                message = "Geçersiz veri.",
                errors,
            }
        );
    }

    private void SendVisitRecordEmail(string email, string subject, string body)
    {
        var smtpClient = new SmtpClient("smtp.turkticaret.net")
        {
            Port = 587,
            Credentials = new NetworkCredential("byb@mutlucanozel.online", "Bybmutlu123."),
            EnableSsl = true,
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress("byb@mutlucanozel.online", "BYB|CRM"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
        };

        mailMessage.To.Add(email);

        try
        {
            smtpClient.Send(mailMessage);
            Console.WriteLine("E-posta başarıyla gönderildi.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"E-posta gönderim hatası: {ex.Message}");
        }
    }

    [HttpPost]
    public async Task<IActionResult> ToggleArchiveStatus(int id)
    {
        var recipe = await _context.Recipes.FindAsync(id);
        if (recipe == null)
            return Json(new { success = false, message = "Reçete bulunamadı." });

        var oldStatus = recipe.ArchiveStatus;
        recipe.ArchiveStatus = (recipe.ArchiveStatus == 1) ? 0 : 1;

        // Kullanıcı ID'sini al
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdString, out var userId);

        // Log oluştur
        _context.RecipeLogs.Add(
            new RecipeLog
            {
                RecipeId = recipe.Id,
                FieldName = "Arşiv Durumu",
                OldValue = oldStatus == 1 ? "Arşivli" : "Arşivsiz",
                NewValue = recipe.ArchiveStatus == 1 ? "Arşivli" : "Arşivsiz",
                CreatedById = userId,
                RecordDate = DateTime.Now,
            }
        );

        await _context.SaveChangesAsync();

        return Json(
            new
            {
                success = true,
                message = recipe.ArchiveStatus == 1
                    ? "Reçete arşivlendi."
                    : "Reçete arşivden çıkarıldı.",
            }
        );
    }

    [HttpGet]
    public async Task<IActionResult> CustomerDetail(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userRoles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();

        if (currentUserId == null)
            return Unauthorized();
        var recipeOfferIds = _context
            .Recipes.Where(r => r.OfferId != null)
            .Select(r => r.OfferId.Value)
            .Distinct()
            .ToList();
        var customer = await _context
            .Customers.Include(c => c.Contacts)
            .Include(c => c.Locations)
            .Include(c => c.Records)
            .Include(c => c.Offers)
            .ThenInclude(o => o.PaperInfo)
            .Include(c => c.Offers)
            .ThenInclude(o => o.AdhesiveInfo)
            .Include(c => c.Offers)
            .ThenInclude(o => o.OrderMethod)
            .Include(c => c.Offers)
            .ThenInclude(o => o.DeliveryMethod)
            .Include(c => c.Recipes)
            .ThenInclude(r => r.RecipeAdditionalProcessings)
            .ThenInclude(rap => rap.AdditionalProcessing)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
            return NotFound();

        if (
            !userRoles.Contains("Yönetici")
            && !userRoles.Contains("GENEL MÜDÜR")
            && !userRoles.Contains("Denetlemeci")
            && customer.CreatedById.ToString() != currentUserId
        )
            return Forbid();

        var changeLogs = await _context
            .ChangeLogs.Where(log => log.RecordId == id && log.TableName == "Customers")
            .OrderByDescending(log => log.ChangedAt)
            .ToListAsync();
        var usedOfferIds = _context
            .Recipes.Where(r => r.OfferId != null)
            .Select(r => r.OfferId.Value)
            .Distinct()
            .ToList();

        ViewBag.UsedOfferIds = usedOfferIds;
        var model = new CustomerDetailViewModel
        {
            Customer = new CustomerViewModel
            {
                Id = customer.Id,
                Sector = customer.Sector,
                Name = customer.Name,
                City = customer.City,
                District = customer.District,
                CreatedBy = _context
                    .Users.Where(u => u.Id == customer.CreatedById)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),

                Note = customer.Note,
            },

            Contacts =
                customer
                    .Contacts?.Select(co => new ContactViewModel
                    {
                        Id = co.Id,
                        CustomerId = co.CustomerId,
                        Title = co.Title,
                        FullName = co.FullName,
                        Gender = co.Gender,
                        IsApproved = co.IsApproved,
                        PhoneNumber = co.PhoneNumber,
                        Email = co.Email,
                    })
                    .ToList() ?? new List<ContactViewModel>(),

            Locations =
                customer
                    .Locations?.Select(lo => new LocationViewModel
                    {
                        Id = lo.Id,
                        CustomerId = lo.CustomerId,
                        Description = lo.Description,
                        Address = lo.Address,
                    })
                    .ToList() ?? new List<LocationViewModel>(),

            Records =
                customer
                    .Records?.Select(re => new RecordViewModel
                    {
                        Id = re.Id,
                        CustomerId = re.CustomerId,
                        Status = re.Status,
                        PlannedDate = re.PlannedDate,
                        ActualDate = re.ActualDate,
                        Information = re.Information,
                    })
                    .ToList() ?? new List<RecordViewModel>(),

            Offers =
                customer
                    .Offers?.OrderByDescending(o => o.Id)
                    .Select(o => new OfferViewModel
                    {
                        Id = o.Id,
                        Width = o.Width,
                        Height = o.Height,
                        PaperInfoId = o.PaperInfoId,
                        PaperInfoName = o.PaperInfo?.Name ?? "Bilinmiyor",
                        AdhesiveInfoId = o.AdhesiveInfoId,
                        AdhesiveInfoName = o.AdhesiveInfo?.Name ?? "Bilinmiyor",
                        OrderMethodId = o.OrderMethodId,
                        OrderMethodName = o.OrderMethod?.Name ?? "Bilinmiyor",
                        OfferStatus = o.OfferStatus,
                        DeliveryMethodId = o.DeliveryMethodId,
                        DeliveryMethodName = o.DeliveryMethod?.Name ?? "Bilinmiyor",
                        PaymentMethod = o.PaymentMethod,
                        IsOfferPresentedToCustomer = o.IsOfferPresentedToCustomer,
                        Price = o.Price,
                        ProductName = o.ProductName,
                        Currency = o.Currency,
                        OrderQuantity = o.OrderQuantity,
                        PaymentTerm = o.PaymentTerm,
                        OfferPicture = o.OfferPicture,

                        // ✅ EŞLEŞEN REÇETEYİ BUL
                        RecipeId = customer.Recipes.FirstOrDefault(r => r.OfferId == o.Id)?.Id,
                    })
                    .ToList() ?? new List<OfferViewModel>(),

            Recipes =
                customer
                    .Recipes?.OrderByDescending(r => r.Id)
                    .Select(r =>
                    {
                        var createdByName =
                            _context
                                .Users.Where(u => u.Id == r.CreatedById)
                                .Select(u => u.FirstName + " " + u.LastName)
                                .FirstOrDefault() ?? "Bilinmiyor";

                        var additionalProcessings = r
                            .RecipeAdditionalProcessings?.Select(
                                rap => new RecipeAdditionalProcessing
                                {
                                    AdditionalProcessingId = rap.AdditionalProcessingId,
                                    AdditionalProcessing = new AdditionalProcessing
                                    {
                                        Name = rap.AdditionalProcessing?.Name ?? "Bilinmiyor",
                                    },
                                }
                            )
                            .ToList();

                        return new Recipe
                        {
                            Id = r.Id,
                            CustomerId = r.CustomerId,
                            RecipeName = r.RecipeName,
                            RecipeCode = r.RecipeCode,
                            Width = r.Width,
                            Height = r.Height,

                            CreatedAt = r.CreatedAt,
                            CurrentStatus = r.CurrentStatus,
                            CreatedById = r.CreatedById,
                            Customer = new Customer { Name = createdByName },
                            RecipeAdditionalProcessings = additionalProcessings,
                        };
                    })
                    .ToList() ?? new List<Recipe>(),

            ChangeLogs = changeLogs,
        };

        var lastInteractionLog = changeLogs
            .Where(log => log.OfferId != null && log.RecordId == id)
            .OrderByDescending(log => log.ChangedAt)
            .FirstOrDefault();

        TimeSpan? timeSinceLastInteraction = null;
        if (lastInteractionLog != null)
        {
            var lastInteractionTime = lastInteractionLog.ChangedAt.ToUniversalTime();
            timeSinceLastInteraction = DateTime.UtcNow - lastInteractionTime;
        }

        string formattedTimeSinceLastInteraction = "Etkileşim bulunamadı";
        if (timeSinceLastInteraction.HasValue)
        {
            var days = (int)timeSinceLastInteraction.Value.TotalDays;
            var hours = timeSinceLastInteraction.Value.Hours;
            var minutes = timeSinceLastInteraction.Value.Minutes;

            var parts = new List<string>();
            if (days > 0)
                parts.Add($"{days} gün");
            if (hours > 0)
                parts.Add($"{hours} saat");
            if (minutes > 0)
                parts.Add($"{minutes} dakika");

            formattedTimeSinceLastInteraction = string.Join(" ", parts);
        }

        ViewBag.TimeSinceLastInteraction = formattedTimeSinceLastInteraction;

        var users = await _context
            .Users.Select(u => new UserViewModel
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
            })
            .ToListAsync();

        ViewBag.Users = users;

        var sectors = await _context.Sectors.ToListAsync();
        ViewBag.Sectors = sectors;

        return View(model);
    }

    [HttpPost]
    public IActionResult AddNoteToCustomer([FromBody] AddNoteRequest request)
    {
        var customer = _context.Customers.Find(request.CustomerId);
        if (customer == null)
        {
            return NotFound("Customer not found.");
        }

        // Mevcut not bilgisini al
        var previousNote = customer.Note;

        // LogChange çağrısı ile not değişikliğini kaydet
        LogChange(
            "Customers",
            customer.Id,
            "Not",
            previousNote ?? string.Empty, // Eski not (boş olabilir)
            request.Note, // Yeni not
            "Güncellendi"
        );

        // Not bilgisini güncelle
        customer.Note = request.Note;
        _context.SaveChanges();

        return Ok();
    }

    public async Task<IActionResult> PotentialCustomerDetail(int id)
    {
        // Giriş yapmış kullanıcının kimliğini ve rollerini alıyoruz
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userRoles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();

        if (currentUserId == null)
        {
            return Unauthorized(); // Kullanıcı oturum açmamışsa 401 Unauthorized döndür
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        ViewData["UserId"] = userId;

        // Müşteri bilgilerini getir
        var customer = await _context
            .Customers.Include(c => c.Contacts)
            .Include(c => c.Locations)
            .Include(c => c.Records)
            .Include(c => c.Offers)
            .ThenInclude(o => o.PaperInfo)
            .Include(c => c.Offers)
            .ThenInclude(o => o.AdhesiveInfo)
            .Include(c => c.Offers)
            .ThenInclude(o => o.OrderMethod)
            .Include(c => c.Offers)
            .ThenInclude(o => o.DeliveryMethod)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
        {
            return NotFound(); // Müşteri bulunamazsa 404 Not Found döndür
        }

        // Yetki kontrolü
        if (
            customer.CreatedById.ToString() == currentUserId
            || userRoles.Contains("Yönetici")
            || userRoles.Contains("Denetlemeci")
        )
        {
            ViewBag.CanViewContacts = true;
            Console.WriteLine(
                "Access Granted: User is either the creator, a manager, or an inspector."
            );
        }
        else
        {
            ViewBag.CanViewContacts = false;
            Console.WriteLine(
                $"Access Denied: CreatedBy({customer.CreatedBy}) != currentUserId({currentUserId}) and user does not have required roles."
            );
        }

        // Müşteriye ait değişiklik loglarını getir (Yetkisiz kullanıcılar için null döner)
        var changeLogs = ViewBag.CanViewContacts
            ? await _context
                .ChangeLogs.Where(log => log.RecordId == id && log.TableName == "Customers")
                .OrderByDescending(log => log.ChangedAt)
                .ToListAsync()
            : null; // Yetkisiz kullanıcılar için null döner

        var model = new CustomerDetailViewModel
        {
            Customer = new CustomerViewModel
            {
                Id = customer.Id,
                Sector = customer.Sector,
                Name = customer.Name,
                City = customer.City,
                District = customer.District,
                IsPotential = customer.IsPotential,
                IsOwned = customer.IsOwned,
                CreatedBy = customer.CreatedBy,
                Note = customer.Note,
                Images = customer.Images.ToList(),
            },
            Contacts = ViewBag.CanViewContacts
                ? customer
                    .Contacts?.Select(co => new ContactViewModel
                    {
                        Id = co.Id,
                        CustomerId = co.CustomerId,
                        Title = co.Title,
                        FullName = co.FullName,
                        Gender = co.Gender,
                        IsApproved = co.IsApproved,
                        PhoneNumber = co.PhoneNumber,
                        Email = co.Email,
                    })
                    .ToList()
                : null, // Yetkisiz kullanıcılar için Contacts null döner
            Locations = customer
                .Locations?.Select(lo => new LocationViewModel
                {
                    Id = lo.Id,
                    CustomerId = lo.CustomerId,
                    Description = lo.Description,
                    Address = lo.Address,
                })
                .ToList(),
            Records = customer
                .Records?.Select(re => new RecordViewModel
                {
                    Id = re.Id,
                    CustomerId = re.CustomerId,
                    Status = re.Status,
                    PlannedDate = re.PlannedDate,
                    ActualDate = re.ActualDate,
                    Information = re.Information,
                })
                .ToList(),
            Offers = customer
                .Offers?.Select(o => new OfferViewModel
                {
                    Id = o.Id,
                    Width = o.Width,
                    Height = o.Height,
                    PaperInfoId = o.PaperInfoId,
                    PaperInfoName = o.PaperInfo?.Name ?? "Bilinmiyor",
                    AdhesiveInfoId = o.AdhesiveInfoId,
                    AdhesiveInfoName = o.AdhesiveInfo?.Name ?? "Bilinmiyor",
                    OrderMethodId = o.OrderMethodId,
                    OrderMethodName = o.OrderMethod?.Name ?? "Bilinmiyor",
                    OfferStatus = o.OfferStatus,
                    DeliveryMethodId = o.DeliveryMethodId,
                    DeliveryMethodName = o.DeliveryMethod?.Name ?? "Bilinmiyor",
                    PaymentMethod = o.PaymentMethod,
                    IsOfferPresentedToCustomer = o.IsOfferPresentedToCustomer,
                    Price = o.Price,
                    Currency = o.Currency,
                    OrderQuantity = o.OrderQuantity,
                    PaymentTerm = o.PaymentTerm,
                    OfferPicture = o.OfferPicture,
                })
                .ToList(),
            ChangeLogs = changeLogs,
        };

        var users = await _context
            .Users.Select(u => new UserViewModel
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
            })
            .ToListAsync();
        ViewBag.Users = users;

        var sectors = await _context.Sectors.ToListAsync();
        ViewBag.Sectors = sectors;

        return View(model);
    }

    [HttpGet]
    [DynamicAuthorize("OfferDetails")]
    public IActionResult OfferDetails(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userRoles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();

        if (currentUserId == null)
        {
            return Unauthorized();
        }

        var offer = _context
            .Offers.Include(o => o.Customer)
            .Include(o => o.PaperInfo)
            .Include(o => o.AdhesiveInfo)
            .Include(o => o.DeliveryMethod)
            .Include(o => o.OrderMethod)
            .FirstOrDefault(o => o.Id == id);

        if (offer == null)
        {
            return NotFound();
        }

        if (
            !userRoles.Contains("Yönetici")
            && !userRoles.Contains("GENEL MÜDÜR")
            && !userRoles.Contains("Denetlemeci")
            && offer.Customer.CreatedById?.ToString() != currentUserId
        )
        {
            return Forbid();
        }

        var createdByUser = _context.Users.FirstOrDefault(u => u.Id == offer.Customer.CreatedById);
        Console.WriteLine($"CreatedByEmail (Controller): {createdByUser?.Email}"); // E-posta adresini kontrol etmek için konsola yazdır

        ViewData["CreatedByEmail"] = createdByUser?.Email ?? "Bilgi yok";

        return View(offer);
    }

    [HttpPost]
    public IActionResult UpdatePrice(int Id, decimal Price, string Currency)
    {
        // İlgili teklifi buluyoruz
        var offer = _context.Offers.FirstOrDefault(o => o.Id == Id);
        if (offer == null)
        {
            return Json(new { success = false, message = "Teklif bulunamadı." });
        }

        if (Price <= 0 || string.IsNullOrEmpty(Currency))
        {
            return Json(
                new
                {
                    success = false,
                    message = "Geçersiz fiyat girdiniz! Lütfen tekrar kontrol ediniz.",
                }
            );
        }

        if (offer.Price == Price && offer.Currency == Currency)
        {
            return Json(
                new { success = false, message = "Girdiğiniz fiyat ve para birimi zaten güncel." }
            );
        }

        try
        {
            var oldPrice = offer.Price;
            var oldCurrency = offer.Currency;

            offer.Price = Price;
            offer.Currency = Currency;

            _context.SaveChanges();

            // Müşteri ID'sini nullable olarak alıyoruz
            int? customerId = offer.CustomerId;

            if (customerId.HasValue)
            {
                // Log kaydı ekliyoruz
                LogChange(
                    "Customers",
                    customerId.Value,
                    $"Birim Fiyat - Teklif ID:{offer.Id}", // Teklif ID'sini ekledik
                    $"{oldPrice?.ToString("0.#####")} {oldCurrency}",
                    $"{Price.ToString("0.#####")} {Currency}",
                    "Güncellendi",
                    offer.Id
                );
            }

            return Json(
                new
                {
                    success = true,
                    newPrice = offer.Price,
                    newCurrency = offer.Currency,
                }
            );
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = $"Güncelleme sırasında bir hata oluştu: {ex.Message}",
                }
            );
        }
    }

    [HttpPost]
    public async Task<IActionResult> SubmitForAdminReview(int offerId)
    {
        try
        {
            Console.WriteLine("Offer ID received in SubmitForAdminReview: " + offerId);

            var offer = await _context
                .Offers.Include(o => o.Customer)
                .Include(o => o.PaperInfo) // PaperInfo navigation property'yi dahil et
                .Include(o => o.AdhesiveInfo) // AdhesiveInfo navigation property'yi dahil et
                .Include(o => o.OrderMethod) // OrderMethod navigation property'yi dahil et
                .Include(o => o.DeliveryMethod) // DeliveryMethod navigation property'yi dahil et
                .FirstOrDefaultAsync(o => o.Id == offerId);

            if (offer != null)
            {
                Console.WriteLine("Offer found with ID: " + offer.Id);

                // Veritabanı işlemi yapmadan doğrudan email gönderme fonksiyonunu çağırıyoruz
                try
                {
                    await SendAdminNotificationEmail(offer);
                    return Json(
                        new { success = true, message = "Teklif yönetici onayına gönderildi 📨" }
                    );
                }
                catch (Exception ex)
                {
                    // Email gönderim sırasında hata oluşursa bu bilgiyi logla ve kullanıcıya ilet
                    Console.WriteLine($"Email gönderimi sırasında hata oluştu: {ex.Message}");
                    return Json(
                        new
                        {
                            success = false,
                            message = $"Email gönderimi başarısız oldu. Hata Detayı: {ex.Message}",
                        }
                    );
                }
            }

            Console.WriteLine("Teklif bulunamadı: " + offerId);
            return Json(new { success = false, message = "Teklif bulunamadı." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Genel hata: {ex.Message}");
            return Json(
                new { success = false, message = $"İşlem sırasında bir hata oluştu: {ex.Message}" }
            );
        }
    }

    private async Task SendAdminNotificationEmail(Offer offer)
    {
        try
        {
            var cultureInfo = new CultureInfo("tr-TR")
            {
                NumberFormat = { NumberGroupSeparator = "." },
            };

            var subject = $"{offer.Customer?.Name ?? "Bilinmeyen Müşteri"} {offer.ProductName}";

            var paymentTermText = offer.PaymentTerm.HasValue
                ? $"{offer.PaymentTerm.Value} gün"
                : "Belirtilmedi";
            var deliveryMethodText = offer.DeliveryMethod?.Name ?? "Belirtilmedi";
            var paymentMethodText = offer.PaymentMethod ?? "Ödeme şekli mevcut değil";

            var imageUrl = string.IsNullOrEmpty(offer.OfferPicture)
                ? ""
                : $"{Request.Scheme}://{Request.Host}/{offer.OfferPicture}";

            // HTML İçeriği
            var htmlContent =
                $@"
<!DOCTYPE html>
<html lang='tr'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Yeni Teklif Bildirimi</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            margin: 0;
            padding: 0;
            background-color: #f4f4f4;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            padding: 20px;
            border-radius: 8px;
        }}
        .header {{
            background-color: #1F253A;
            color: white;
            text-align: center;
            padding: 10px 0;
            font-size: 20px;
            border-radius: 16px 16px 0 0;
        }}
        .content p {{
            margin: 5px 30px;
            line-height: 2.1;
        }}
        .button {{
            display: block;
            width: 80%;
            margin: 20px auto;
            text-align: center;
            background-color: #1F253A;
            color: white;
            padding: 10px;
            border-radius: 10px;
            text-decoration: none;
            font-weight: bold;
        }}
        .footer {{
            background-color: #1F253A;
            color: white;
            text-align: center;
            padding: 10px;
            border-radius: 0 0 16px 16px;
            font-size: 14px;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>📝 {offer.ProductName}</div>
        <div class='content'>
            <p> <strong><br>{offer.Customer?.Name ?? "Müşteri bilgisi mevcut değil"}</strong> firması <br>{offer.ProductName} ürününden <br>
            {offer.OrderQuantity.ToString("N0", cultureInfo)}
            {(string.IsNullOrEmpty(offer.OrderMethod?.Name) ? "(Sipariş yöntemi mevcut değil)" : $" {offer.OrderMethod.Name} için teklif istemiştir.")}
            <br><br>Teşekkürler<br>{offer.Customer.CreatedBy} </p>

          <a href='{Url.Action("OfferDetails", "Customer", new { id = offer.Id, }, protocol: Request.Scheme)}' 
    class='button'>Detayları İncele 🔎</a>

{(string.IsNullOrEmpty(imageUrl) ? "" : $"<img src='{imageUrl}' style='max-width:100%; border-radius: 8px; display: block; margin: 20px auto;' alt='Teklif Resmi' />")}

        </div>
        <div class='footer'>
            Copyright © 2024 | BYB CRM
        </div>
    </div>
</body>
</html>";

            // Veritabanından admin e-posta adreslerini alın
            List<string> adminEmails = await _context.MailInfos.Select(m => m.Mail).ToListAsync();

            // SMTP istemcisi ayarları
            var smtpClient = new SmtpClient("smtp.turkticaret.net")
            {
                Port = 587,
                Credentials = new NetworkCredential("byb@mutlucanozel.online", "Bybmutlu123."),
                EnableSsl = true,
            };
            var fromName = "BYB|CRM YENİ TEKLİF 🎊";

            // E-posta Mesajı
            var mailMessage = new MailMessage
            {
                From = new MailAddress("byb@mutlucanozel.online", fromName),
                Subject = subject,
                Body = htmlContent,
                IsBodyHtml = true,
            };

            // Admin e-posta adreslerini ekleyin
            foreach (var email in adminEmails)
            {
                mailMessage.To.Add(email);
            }

            // E-postayı gönderin
            await smtpClient.SendMailAsync(mailMessage);

            Console.WriteLine("Email gönderildi.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Mail gönderimi hatası: {ex.Message}");
            throw new Exception($"Mail gönderimi hatası: {ex.Message}");
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendPriceNotification(
        int Id,
        [FromServices] WhatsAppService whatsAppService
    )
    {
        Console.WriteLine($"ID received for price notification: {Id}");

        if (Id == 0)
        {
            return Json(new { success = false, message = "Geçersiz Teklif ID'si." });
        }

        var offer = await _context
            .Offers.Include(o => o.Customer)
            .Include(o => o.OrderMethod)
            .Include(o => o.DeliveryMethod)
            .FirstOrDefaultAsync(o => o.Id == Id);

        if (offer == null)
        {
            return Json(new { success = false, message = "Teklif bulunamadı." });
        }

        try
        {
            var responsibleUser = await _context
                .Users.Where(u => u.Id == offer.Customer.CreatedById)
                .Select(u => new { u.Email, u.PhoneNumber })
                .FirstOrDefaultAsync();

            if (responsibleUser == null || string.IsNullOrWhiteSpace(responsibleUser.Email))
            {
                return Json(new { success = false, message = "Müşteri sorumlusu bulunamadı." });
            }

            string messageType;

            if (!offer.IsPriceEntered)
            {
                messageType = "Eklendi";
                offer.IsPriceEntered = true;
                await _context.SaveChangesAsync();
            }
            else
            {
                messageType = "Güncellendi";
            }

            try
            {
                await SendNotificationEmail(
                    offer,
                    responsibleUser.Email,
                    responsibleUser.PhoneNumber,
                    messageType,
                    whatsAppService
                );
            }
            catch (Exception innerEx)
            {
                Console.WriteLine("Bildirim gönderimi sırasında hata: " + innerEx.Message);
                return Json(
                    new
                    {
                        success = false,
                        message = "Bildirim gönderimi başarısız.",
                        error = innerEx.Message,
                    }
                );
            }

            return Json(
                new { success = true, message = "Fiyat bildirimi başarıyla gönderildi 📧💬" }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine("Genel hata: " + ex.Message);
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }

    private async Task SendNotificationEmail(
        Offer offer,
        string responsibleEmail,
        string responsiblePhone,
        string messageType,
        WhatsAppService whatsAppService // artık kullanılmayacak ama imzada kalabilir
    )
    {
        try
        {
            var cultureInfo = new CultureInfo("tr-TR")
            {
                NumberFormat = { NumberGroupSeparator = "." },
            };

            var orderQuantity = offer.OrderQuantity.ToString("N0", cultureInfo);
            var orderMethod = offer.OrderMethod?.Name ?? "Belirtilmedi";
            var deliveryMethod = offer.DeliveryMethod?.Name ?? "Belirtilmedi";

            var titleAction =
                messageType == "Eklendi" ? "Fiyat Girişi Yapıldı" : "Fiyat Güncellemesi Yapıldı";
            var subject = $"{offer.Customer?.Name ?? "Bilinmeyen Müşteri"} için {titleAction} - ";

            var htmlContent =
                $@"
<!DOCTYPE html>
<html lang='tr'>
<head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'></head>
<body style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
<div style='max-width: 600px; margin: auto; background-color: white; padding: 20px; border-radius: 8px;'>
<h2 style='background-color: #1F253A; color: white; text-align: center; padding: 10px;'>📝 {offer.ProductName}</h2>
<p>Fiyat bilgisi <strong>{offer.Customer?.Name ?? "Müşteri bilgisi mevcut değil"}</strong> müşterisi için {messageType.ToLower()}.</p>
<a href='{Url.Action("OfferDetails", "Customer", new { id = offer.Id }, Request.Scheme)}'
style='display: block; width: 80%; margin: 20px auto; text-align: center; background-color: #1F253A;
color: white; padding: 10px; border-radius: 10px; text-decoration: none;'>Detayları İncele 🔎</a>
</div></body></html>";

            var smtpClient = new SmtpClient("smtp.turkticaret.net")
            {
                Port = 587,
                Credentials = new NetworkCredential("info@mutlucanozel.online", "Mutlu12345*"),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress("info@mutlucanozel.online", $"BYB|CRM {titleAction} 🎊"),
                Subject = subject,
                Body = htmlContent,
                IsBodyHtml = true,
            };

            mailMessage.To.Add(responsibleEmail);
            await smtpClient.SendMailAsync(mailMessage);
            Console.WriteLine("📧 Email başarıyla gönderildi.");

            // WhatsApp gönderimi kaldırıldı (iptal edildi)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"📧 E-posta gönderim hatası: {ex.Message}");
            throw;
        }
    }

    [HttpPost]
    [DynamicAuthorize("ApproveContact")]
    public IActionResult ApproveContact(int id)
    {
        try
        {
            // Find the contact by ID ve müşteri bilgisini dahil et
            var contact = _context
                .Contacts.Include(c => c.Customer) // Müşteri bilgisine erişim
                .FirstOrDefault(c => c.Id == id);

            if (contact == null)
            {
                return Json(new { success = false, message = "İrtibat bulunamadı." });
            }

            // Önceki onay durumunu kaydet (Onaylı / Onaysız olarak)
            string previousApprovalState = contact.IsApproved ? "Onaylı" : "Onaysız";

            // Onay durumunu değiştir
            contact.IsApproved = !contact.IsApproved;
            _context.Contacts.Update(contact);
            _context.SaveChanges();

            // Yeni onay durumunu belirle (Onaylı / Onaysız olarak)
            string newApprovalState = contact.IsApproved ? "Onaylı" : "Onaysız";

            // Değişiklik mesajını belirle
            string message = contact.IsApproved
                ? "İrtibat başarıyla onaylandı."
                : "İrtibat onayı kaldırıldı.";

            // Müşteri kontrolü
            var customerId = contact.Customer != null ? contact.Customer.Id : 0;

            // Değişiklik günlüğünü ekle (AddOffer'daki gibi)
            LogChange(
                "Customers", // Entity adı (dinamik müşteri varlığı)
                customerId, // Müşteri Id'si
                contact.IsApproved
                    ? $"İrtibat Onaylandı - İrtibat ID: {contact.Id}"
                    : $"İrtibat Onayı Kaldırıldı - İrtibat ID: {contact.Id}",
                $" {previousApprovalState}", // Önceki durum (Onaylı / Onaysız)
                $" {newApprovalState}", // Yeni durum (Onaylı / Onaysız)
                "Güncellendi", // Değişiklik açıklaması
                contact.Id // Kayıt ID'si (irşbat)
            );

            return Json(
                new
                {
                    success = true,
                    isApproved = contact.IsApproved,
                    message,
                }
            );
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Bir hata oluştu: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddContact(ContactViewModel model)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var customerExists = _context.Customers.Any(c => c.Id == model.CustomerId);
                if (!customerExists)
                {
                    return Json(new { success = false, message = "Geçersiz müşteri ID'si." });
                }

                var contact = new Contact
                {
                    CustomerId = model.CustomerId,
                    Title = model.Title,
                    FullName = model.FullName,
                    Gender = model.Gender,
                    IsApproved = model.IsApproved,
                    PhoneNumber = model.PhoneNumber,
                    Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email, // Email boşsa null yap
                };

                _context.Contacts.Add(contact);
                await _context.SaveChangesAsync();

                LogChange(
                    "Customers",
                    model.CustomerId,
                    $"İletişim - {contact.Id}",
                    string.Empty,
                    $"Ad Soyad: {contact.FullName}{Environment.NewLine}"
                        + $"Telefon: {contact.PhoneNumber}{Environment.NewLine}"
                        + $"Email: {contact.Email}",
                    "Oluşturuldu"
                );

                return Json(new { success = true });
            }

            // ModelState'deki hataları al
            var errors = ModelState
                .Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            // Hatalar var ise, doğrudan hata mesajlarını döndür
            return Json(new { success = false, errors });
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Beklenmeyen bir hata oluştu.",
                    error = ex.Message,
                }
            );
        }
    }

    [HttpPost]
    public IActionResult EditCustomerCreatedBy(int id, string createdBy, int createdById)
    {
        // Log the incoming parameters to verify values
        _logger.LogInformation($"ID: {id}, CreatedBy: {createdBy}, CreatedById: {createdById}");

        if (string.IsNullOrEmpty(createdBy) || createdById <= 0)
        {
            return Json(new { success = false, message = "Geçersiz veri" });
        }

        var customer = _context.Customers.Find(id);
        if (customer == null)
        {
            return Json(new { success = false, message = "Müşteri bulunamadı" });
        }

        var oldCreatedBy = customer.CreatedBy;
        var oldCreatedById = customer.CreatedById;

        customer.CreatedBy = createdBy;
        customer.CreatedById = createdById;
        customer.IsOwned = true;

        // Değişiklikleri logla
        AddLogIfChanged("CreatedBy", oldCreatedBy, createdBy, customer.Id);

        _context.SaveChanges();

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> AddLocation(LocationViewModel model)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var customerExists = _context.Customers.Any(c => c.Id == model.CustomerId);
                if (!customerExists)
                {
                    return Json(new { success = false, message = "Geçersiz müşteri ID'si." });
                }

                // Yeni konum ekle
                var location = new Location
                {
                    CustomerId = model.CustomerId,
                    Address = model.Address,
                    Description = model.Description,
                };

                _context.Locations.Add(location);
                await _context.SaveChangesAsync();

                // Loglama işlemi
                LogChange(
                    "Customers",
                    model.CustomerId,
                    $"Konum - {location.Id}",
                    string.Empty, // Eski değer yok
                    $"Adres: {location.Address}{Environment.NewLine}"
                        + $"Tanım: {location.Description}",
                    "Oluşturuldu"
                );

                return Json(new { success = true });
            }

            // ModelState hatalarını topla
            var errors = ModelState
                .Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            // Hataları doğrudan döndür
            return Json(new { success = false, errors });
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Beklenmeyen bir hata oluştu.",
                    error = ex.Message,
                }
            );
        }
    }

    [HttpPost]
    public async Task<IActionResult> CheckCustomerContact(int customerId)
    {
        var customer = await _context
            .Customers.Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == customerId);

        if (customer == null)
        {
            return Json(new { hasContact = false });
        }

        var hasContact = customer.Contacts != null && customer.Contacts.Any();
        return Json(new { hasContact });
    }

    [HttpPost]
    public async Task<IActionResult> AddOffer(IFormFile offerPicture, OfferViewModel model)
    {
        ModelState.Remove("CustomerId");
        ModelState.Remove("PaymentTerm");
        ModelState.Remove("DeliveryMethodId");
        ModelState.Remove("OfferStatus");
        ModelState.Remove("OfferPicture");
        ModelState.Remove("AdditionalProcessingIds");
        ModelState.Remove(nameof(model.AdditionalProcessing));

        if (ModelState.IsValid)
        {
            // Müşteri kontrolü
            var customer = await _context.Customers.FindAsync(model.CustomerId);
            if (customer == null)
            {
                return Json(new { success = false, message = "Müşteri bulunamadı." });
            }

            var offer = new Offer
            {
                CustomerId = model.CustomerId,
                Width = model.Width,
                Height = model.Height,
                PaperInfoId = model.PaperInfoId,
                AdhesiveInfoId = model.AdhesiveInfoId,
                DeliveryMethodId = model.DeliveryMethodId,
                PaymentMethod = model.PaymentMethod,
                OrderMethodId = model.OrderMethodId,
                OrderQuantity = model.OrderQuantity,
                ProductName = model.ProductName,
                Description = model.Description,
                IsPrinted = model.IsPrinted,
                NumberOfColors = model.NumberOfColors,
                PaymentTerm = model.PaymentTerm,
                AdditionalProcessing =
                    model.AdditionalProcessingIds != null && model.AdditionalProcessingIds.Any()
                        ? string.Join(",", model.AdditionalProcessingIds)
                        : null,
            };

            if (offerPicture != null && offerPicture.Length > 0)
            {
                // Yükleme klasörünü belirle
                var uploadsFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads"
                );

                // Benzersiz dosya adı oluştur ve uzantıyı .webp olarak belirle
                var uniqueFileName = $"{Guid.NewGuid()}.webp";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Eğer klasör yoksa oluştur
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // Geçici bir dosya yolu oluştur
                var tempFilePath = Path.Combine(
                    uploadsFolder,
                    $"temp-{Guid.NewGuid()}{Path.GetExtension(offerPicture.FileName)}"
                );

                // Dosyayı geçici olarak kaydet
                using (var tempStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await offerPicture.CopyToAsync(tempStream);
                }

                using (
                    SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(tempFilePath)
                )
                {
                    image.Save(filePath, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder());
                }
                // Geçici dosyayı sil
                System.IO.File.Delete(tempFilePath);

                // Modelin OfferPicture özelliğini güncelle
                offer.OfferPicture = $"/uploads/{uniqueFileName}";
            }
            _context.Offers.Add(offer);
            await _context.SaveChangesAsync();

            // Log kaydı ekleme

            LogChange(
                "Customers",
                offer.Customer.Id,
                $"Teklif Eklendi - {offer.Id}",
                "",
                offer.ProductName,
                "Oluşturuldu",
                offer.Id
            );

            return Json(
                new
                {
                    success = true,
                    message = "Teklif başarıyla eklendi.",
                    offerId = offer.Id,
                }
            );
        }

        var errors = ModelState
            .Values.SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .ToList();

        return Json(
            new
            {
                success = false,
                message = "Gerekli alanları doldurunuz",
                errors,
            }
        );
    }

    // Kullanıcı ID çekme yardımcı fonksiyon
    private int GetCurrentUserId()
    {
        // Kullanıcının ID'sini ClaimTypes.NameIdentifier üzerinden alıyoruz
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdString, out int userId))
        {
            throw new Exception("Kullanıcı ID conversion failed.");
            // veya senin dediğin gibi istersen View döndür ama genelde burada Exception atılır
        }

        return userId;
    }

    [HttpPost]
    public async Task<IActionResult> UpdateRecipeStatus(int id, int status)
    {
        var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe == null)
            return Json(new { success = false, message = "Reçete bulunamadı." });

        int oldStatus = recipe.CurrentStatus ?? 0;

        recipe.CurrentStatus = status;

        int userId = GetCurrentUserId();
        DateTime now = DateTime.Now;

        var statusText = new Dictionary<int, string>
        {
            { 1, "Reçete Kaydı Yapıldı" },
            { 2, "Grafiker İşlemi Bekliyor" },
            { 3, "Grafiker İşleminde" },
            { 4, "Müşteri Onayı Bekliyor" },
            { 5, "Müşteri Onaylı Reçete" },
            { 6, "Montaj Bekliyor" },
            { 7, "Bıçak / Klişe Bekliyor" },
            { 8, "Üretime Hazır Reçete" },
            { 98, "İptal Talebi" },
            { 99, "İptal Edildi" },
        };

        // Eski ve yeni değerleri al
        string oldText = statusText.TryGetValue(oldStatus, out var txtOld)
            ? txtOld
            : oldStatus.ToString();
        string newText = statusText.TryGetValue(status, out var txtNew)
            ? txtNew
            : status.ToString();

        // Durum değişmişse logla
        if (oldStatus != status)
        {
            var log = new RecipeLog
            {
                RecipeId = recipe.Id,
                FieldName = "Reçete Durumu",
                OldValue = oldText,
                NewValue = newText,
                CreatedById = userId,
                RecordDate = now,
            };

            _context.RecipeLogs.Add(log);
        }

        try
        {
            var loggedInUser = await _context
                .Users.Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (
                loggedInUser != null
                && loggedInUser.UserRoles.Any(ur =>
                    ur.Role.Equals("grafiker", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                // Grafiker atanması değiştiyse logla
                if (recipe.DesignerId != loggedInUser.Id)
                {
                    var log = new RecipeLog
                    {
                        RecipeId = recipe.Id,
                        FieldName = "Grafiker",
                        OldValue = recipe.DesignerId?.ToString() ?? "-",
                        NewValue = userId.ToString(),
                        CreatedById = userId,
                        RecordDate = now,
                    };
                    _context.RecipeLogs.Add(log);
                }

                recipe.DesignerId = loggedInUser.Id;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DesignerId atama hatası: {ex.Message}");
        }

        await _context.SaveChangesAsync();

        return Json(new { success = true, message = "Durum başarıyla güncellendi." });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateRecipe(Recipe model)
    {
        var recipe = await _context
            .Recipes.Include(r => r.RecipeAdditionalProcessings)
            .Include(r => r.PaperInfo)
            .FirstOrDefaultAsync(r => r.Id == model.Id);

        if (recipe == null)
            return Json(new { success = false, message = "Reçete bulunamadı." });

        int userId = GetCurrentUserId();
        DateTime now = DateTime.Now;
        var logs = new List<RecipeLog>();
        void LogIfChanged<T>(string fieldName, string label, T oldVal, T newVal)
        {
            string oldStr = FormatValue(oldVal);
            string newStr = FormatValue(newVal);

            if (oldStr != newStr)
            {
                logs.Add(
                    new RecipeLog
                    {
                        RecipeId = recipe.Id,
                        FieldName = label,
                        OldValue = oldStr,
                        NewValue = newStr,
                        CreatedById = userId,
                        RecordDate = now,
                    }
                );
            }
        }

        string FormatValue<T>(T value)
        {
            if (value == null)
                return "-";

            if (value is decimal d)
                return d.ToString("0.##");
            if (value is double dbl)
                return dbl.ToString("0.##");
            if (value is float f)
                return f.ToString("0.##");

            return value.ToString();
        }

        // Alan karşılaştırmaları
        LogIfChanged("RecipeName", "Reçete Adı", recipe.RecipeName, model.RecipeName);
        LogIfChanged("Width", "Etiket En", recipe.Width, model.Width);
        LogIfChanged("Height", "Etiket Boy", recipe.Height, model.Height);
        var oldQuantity = recipe.Quantity;
        var newQuantity = model.Quantity;

        var oldUnitName = await _context
            .OrderMethods.Where(u => u.Id == recipe.UnitId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync();

        var newUnitName = await _context
            .OrderMethods.Where(u => u.Id == model.UnitId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync();

        // 🔁 Eğer miktar veya birim değiştiyse tek log satırı yaz
        if (oldQuantity != newQuantity || oldUnitName != newUnitName)
        {
            var oldVal =
                $"{(oldQuantity?.ToString("N0", new CultureInfo("tr-TR")) ?? "-")} {oldUnitName ?? "-"}";
            var newVal =
                $"{(newQuantity?.ToString("N0", new CultureInfo("tr-TR")) ?? "-")} {newUnitName ?? "-"}";

            LogIfChanged("QuantityUnit", "Sipariş Miktarı ve Birimi", oldVal, newVal);
        }

        var oldPaperName = recipe.PaperInfo?.Name;
        var newPaperName = await _context
            .PaperInfos.Where(p => p.Id == model.PaperTypeId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();

        LogIfChanged("PaperTypeId", "Kağıt Cinsi", oldPaperName, newPaperName);
        var oldAdhesionTypeName = await _context
            .AdhesiveInfos.Where(p => p.Id == recipe.PaperAdhesionTypeId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();

        var newAdhesionTypeName = await _context
            .AdhesiveInfos.Where(p => p.Id == model.PaperAdhesionTypeId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();

        LogIfChanged(
            "PaperAdhesionTypeId",
            "Tutkal Cinsi",
            oldAdhesionTypeName,
            newAdhesionTypeName
        );
        var oldPaperDetailName = await _context
            .PaperDetails.Where(p => p.Id == recipe.PaperDetailId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();
        var newPaperDetailName = await _context
            .PaperDetails.Where(p => p.Id == model.PaperDetailId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync();
        LogIfChanged("PaperDetailId", "Kağıt Detay", oldPaperDetailName, newPaperDetailName);
        LogIfChanged(
            "CustomerAdhesionTypeId",
            "Müşteri Yapıştırma",
            recipe.CustomerAdhesionTypeId,
            model.CustomerAdhesionTypeId
        );
        LogIfChanged("PackageTypeId", "Paketleme", recipe.PackageTypeId, model.PackageTypeId);
        LogIfChanged("LabelPerWrap", "Sarım Etiket Adedi", recipe.LabelPerWrap, model.LabelPerWrap);
        LogIfChanged("OuterDiameter", "Sarım Dış Çap", recipe.OuterDiameter, model.OuterDiameter);
        LogIfChanged("CustomerCode", "Müşteri Kodu", recipe.CustomerCode, model.CustomerCode);
        LogIfChanged("CoreLengthId", "Kuka Uzunluğu", recipe.CoreLengthId, model.CoreLengthId);
        LogIfChanged("CoreDiameterId", "Kuka Çapı", recipe.CoreDiameterId, model.CoreDiameterId);
        LogIfChanged(
            "ShipmentMethodId",
            "Sevkiyat Şekli",
            recipe.ShipmentMethodId,
            model.ShipmentMethodId
        );
        LogIfChanged(
            "WindingDirectionType",
            "Sarım Yönü",
            recipe.WindingDirectionType,
            model.WindingDirectionType
        );
        LogIfChanged(
            "NoteToDesigner",
            "Grafiker Notu",
            recipe.NoteToDesigner,
            model.NoteToDesigner
        );
        LogIfChanged(
            "NoteForProduction",
            "Üretim Notu",
            recipe.NoteForProduction,
            model.NoteForProduction
        );

        // Alan güncellemeleri
        recipe.RecipeName = model.RecipeName;
        recipe.Width = model.Width;
        recipe.Height = model.Height;
        recipe.Quantity = model.Quantity;
        recipe.UnitId = model.UnitId;
        recipe.PaperTypeId = model.PaperTypeId;
        recipe.PaperAdhesionTypeId = model.PaperAdhesionTypeId;
        recipe.PaperDetailId = model.PaperDetailId;
        recipe.CustomerCode = model.CustomerCode;
        recipe.CustomerAdhesionTypeId = model.CustomerAdhesionTypeId;
        recipe.PackageTypeId = model.PackageTypeId;
        recipe.LabelPerWrap = model.LabelPerWrap;
        recipe.OuterDiameter = model.OuterDiameter;
        recipe.CoreLengthId = model.CoreLengthId;
        recipe.CoreDiameterId = model.CoreDiameterId;
        recipe.ShipmentMethodId = model.ShipmentMethodId;
        recipe.WindingDirectionType = model.WindingDirectionType;
        recipe.NoteToDesigner = model.NoteToDesigner;
        recipe.NoteForProduction = model.NoteForProduction;

        // Grafiker atama
        try
        {
            var loggedInUser = await _context
                .Users.Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (
                loggedInUser != null
                && loggedInUser.UserRoles.Any(ur => ur.Role.ToLower() == "grafiker")
            )
            {
                if (recipe.DesignerId != loggedInUser.Id)
                {
                    logs.Add(
                        new RecipeLog
                        {
                            RecipeId = recipe.Id,
                            FieldName = "Grafiker",
                            OldValue = recipe.DesignerId?.ToString() ?? "-",
                            NewValue = loggedInUser.Id.ToString(),
                            CreatedById = userId,
                            RecordDate = now,
                        }
                    );
                }

                recipe.DesignerId = loggedInUser.Id;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DesignerId atama hatası: {ex.Message}");
        }

        // İlave işlem güncelleme
        var selectedProcessingIds = Request
            .Form["AdditionalProcessing"]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

        var newProcessing = selectedProcessingIds.Any()
            ? string.Join(",", selectedProcessingIds)
            : null;

        LogIfChanged(
            "AdditionalProcessing",
            "İlave İşlem",
            recipe.AdditionalProcessing,
            newProcessing
        );

        recipe.AdditionalProcessing = newProcessing;

        _context.RecipeAdditionalProcessings.RemoveRange(recipe.RecipeAdditionalProcessings);
        recipe.RecipeAdditionalProcessings = selectedProcessingIds
            .Select(id => new RecipeAdditionalProcessing
            {
                RecipeId = recipe.Id,
                AdditionalProcessingId = id,
            })
            .ToList();

        // Log kayıtlarını ekle
        if (logs.Any())
        {
            _context.RecipeLogs.AddRange(logs);
        }

        await _context.SaveChangesAsync();
        return Json(
            new
            {
                success = true,
                message = "Reçete başarıyla güncellendi.",
                id = recipe.Id,
            }
        );
    }

    [HttpGet]
    public async Task<IActionResult> GetRecipeById(int id)
    {
        var recipe = await _context
            .Recipes.Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.CustomerId,
                CustomerName = r.Customer.Name,
                r.RecipeName,
                r.CustomerCode,
                r.RecipeCode,
                r.Width,
                r.Height,
                r.Quantity,
                r.UnitId,
                r.PaperTypeId,
                r.PaperAdhesionTypeId,
                r.PaperDetailId,
                r.CustomerAdhesionTypeId,
                r.PackageTypeId,
                r.LabelPerWrap,
                r.OuterDiameter,
                r.CoreLengthId,
                r.CoreDiameterId,
                r.ShipmentMethodId,

                // ✅ AdditionalProcessings many-to-many üzerinden çekiliyor
                AdditionalProcessings = r.RecipeAdditionalProcessings.Select(rap => new
                {
                    rap.AdditionalProcessing.Id,
                    rap.AdditionalProcessing.Name,
                }),

                r.WindingDirectionType,
                r.NoteToDesigner,
                r.NoteForProduction,
            })
            .FirstOrDefaultAsync();

        if (recipe == null)
        {
            return Json(new { success = false, message = "Reçete bulunamadı." });
        }

        return Json(new { success = true, data = recipe });
    }

    [HttpGet]
    public async Task<IActionResult> GetDesignerInfo(int id)
    {
        var recipe = await _context
            .Recipes.Include(r => r.Knife)
            .Include(r => r.MoldCliche)
            .Include(r => r.RecipeMachines)
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                DesignerFullName = r.Designer,
                r.DesignerNote,
                r.NoteToDesigner,
                r.Format,
                r.ColorCyan,
                r.ColorMagenta,
                r.ColorYellow,
                r.ColorKey,
                r.ExtraColor1,
                r.ExtraColor2,
                r.ExtraColor3,
                r.ExtraColor4,
                r.ExtraColor5,
                r.DieCutPlateTypeId,
                DieCutPlateTypeName = r.MoldCliche != null ? r.MoldCliche.Name : null,
                r.PlateCount,
                r.PlateNumber,
                r.PaperWidth,
                r.MountQtyYY,
                r.MountQtyAA,
                r.KnifeTypeId,
                KnifeTypeName = r.Knife != null ? r.Knife.Name : null,
                r.KnifeCode,
                r.SerialNumber,
                r.ZetDiameter,
                r.KnifeYY,
                r.KnifeAA,
                r.KnifeYYAB,
                r.Radius,
                selectedMachineIds = r.RecipeMachines.Select(rm => rm.MachineId).ToList(), // ✅ EKLENDİ
            })
            .FirstOrDefaultAsync();

        if (recipe == null)
            return Json(new { success = false, message = "Reçete bulunamadı." });

        return Json(new { success = true, data = recipe });
    }

    [HttpPost]
    public async Task<IActionResult> AddSector(SectorViewModel model)
    {
        if (ModelState.IsValid)
        {
            // Sektörün zaten var olup olmadığını kontrol et
            var existingSector = await _context.Sectors.FirstOrDefaultAsync(s =>
                s.Name == model.Name
            );

            if (existingSector != null)
            {
                return Json(new { success = false, message = "Bu sektör zaten mevcut." });
            }

            // Yeni sektör oluştur
            var sector = new Sector { Name = model.Name.ToUpper(CultureInfo.CurrentCulture) };

            _context.Sectors.Add(sector);
            await _context.SaveChangesAsync(); // Kaydet

            // Log kaydı ekle
            LogChange("", 0, "Sectors", string.Empty, $"{sector.Name}", "Oluşturuldu");

            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Geçersiz veri" });
    }

    [HttpPost]
    public IActionResult UpdateOfferPresentedStatus(int id, OfferStatus offerStatus)
    {
        var offer = _context.Offers.Include(o => o.Customer).FirstOrDefault(o => o.Id == id);

        if (offer == null || offer.Customer == null)
        {
            return Json(new { success = false, message = "Teklif veya müşteri bulunamadı." });
        }

        if (
            !offer.Price.HasValue
            && (offerStatus == OfferStatus.Delivered || offerStatus == OfferStatus.Won)
        )
        {
            return Json(
                new { success = false, message = "Fiyat bilgisi olmadan bu durum seçilemez." }
            );
        }

        var oldStatus = offer.OfferStatus;
        offer.OfferStatus = offerStatus;

        try
        {
            _context.SaveChanges();

            LogChange(
                "Customers",
                offer.Customer.Id,
                $"Teklif ID:{offer.Id} - Durum",
                oldStatus.GetDisplayName(),
                offer.OfferStatus.GetDisplayName(),
                "Güncellendi",
                offer.Id
            );

            return Json(
                new
                {
                    success = true,
                    message = "Teklif durumu başarıyla güncellendi.",
                    updatedOffer = new
                    {
                        offer.Id,
                        StatusText = offer.OfferStatus.GetDisplayName(),
                    },
                }
            );
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Bir hata oluştu, lütfen tekrar deneyin.",
                    error = ex.Message,
                }
            );
        }
    }

    public IActionResult GetChangeLogsByOfferId(int offerId)
    {
        var logs = _context
            .ChangeLogs.Where(log => log.OfferId == offerId)
            .Select(log => new
            {
                Date = log.ChangedAt.ToString("dd.MM.yyyy HH:mm"), // Tarihi formatlayarak döndür
                Field = LogHelper.GetTranslatedColumnName(log.ColumnName),
                OldValue = log.OldValue,
                NewValue = log.NewValue,
                OperationType = log.OperationType,
                ChangedBy = log.ChangedBy,
            })
            .ToList();

        return Json(logs);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOffer(IFormFile offerPicture, OfferViewModel model)
    {
        ModelState.Remove("PaymentTerm");
        ModelState.Remove("DeliveryMethodId");
        ModelState.Remove("OfferPicture");
        ModelState.Remove("AdditionalProcessingIds");

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return Json(
                new
                {
                    success = false,
                    message = "Gerekli alanları doldurunuz",
                    errors,
                }
            );
        }

        var offer = await _context
            .Offers.Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == model.Id);
        if (offer == null)
        {
            return Json(new { success = false, message = "Teklif bulunamadı." });
        }

        var oldOffer = new Offer
        {
            CustomerId = offer.CustomerId,
            Width = offer.Width,
            Height = offer.Height,
            PaperInfoId = offer.PaperInfoId,
            AdhesiveInfoId = offer.AdhesiveInfoId,
            DeliveryMethodId = offer.DeliveryMethodId,
            PaymentMethod = offer.PaymentMethod,
            OrderMethodId = offer.OrderMethodId,
            OrderQuantity = offer.OrderQuantity,
            ProductName = offer.ProductName,
            IsPrinted = offer.IsPrinted,
            NumberOfColors = offer.NumberOfColors,
            Description = offer.Description,
            PaymentTerm = offer.PaymentTerm,
            AdditionalProcessing = offer.AdditionalProcessing,
        };

        // Yeni değerlerle güncelleme yapıyoruz.
        offer.CustomerId = model.CustomerId;
        offer.Width = model.Width;
        offer.Height = model.Height;
        offer.PaperInfoId = model.PaperInfoId;
        offer.AdhesiveInfoId = model.AdhesiveInfoId;
        offer.DeliveryMethodId = model.DeliveryMethodId;
        offer.PaymentMethod = model.PaymentMethod;
        offer.OrderMethodId = model.OrderMethodId;
        offer.OrderQuantity = model.OrderQuantity;
        offer.ProductName = model.ProductName;
        offer.Description = model.Description;
        offer.IsPrinted = model.IsPrinted;
        offer.NumberOfColors = model.NumberOfColors;
        offer.PaymentTerm = model.PaymentTerm;

        offer.AdditionalProcessing =
            model.AdditionalProcessingIds != null && model.AdditionalProcessingIds.Any()
                ? string.Join(",", model.AdditionalProcessingIds)
                : null;

        try
        {
            if (offerPicture != null && offerPicture.Length > 0)
            {
                var uploadsFolder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads"
                );
                var fileName = Path.GetFileName(offerPicture.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await offerPicture.CopyToAsync(stream);
                }

                offer.OfferPicture = $"/uploads/{fileName}";
            }

            // Log kaydı ekle
            LogChangesForCustomer(offer.Customer.Id, oldOffer, offer);

            _context.Offers.Update(offer);
            await _context.SaveChangesAsync();
            return Json(
                new
                {
                    success = true,
                    message = "Teklif başarıyla güncellendi.",
                    offerId = offer.Id,
                }
            );
        }
        catch (Exception ex)
        {
            return Json(
                new
                {
                    success = false,
                    message = "Teklifi güncellerken bir hata oluştu: " + ex.Message,
                }
            );
        }
    }

    private void LogChangesForCustomer(int customerId, Offer oldOffer, Offer newOffer)
    {
        string offerIdentifier = $"{newOffer.Id}";

        // Ürün Adı değişikliği
        if (oldOffer.ProductName != newOffer.ProductName)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Ürün Adı",
                oldOffer.ProductName,
                newOffer.ProductName,
                "Güncellendi",
                newOffer.Id
            );
        }

        // Sipariş Miktarı değişikliği
        if (oldOffer.OrderQuantity != newOffer.OrderQuantity)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Sipariş Miktarı",
                oldOffer.OrderQuantity.ToString(),
                newOffer.OrderQuantity.ToString(),
                "Güncellendi"
            );
        }
        if (oldOffer.Description != newOffer.Description)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Açıklama",
                oldOffer.Description ?? "",
                newOffer.Description ?? "",
                "Güncellendi"
            );
        }

        // En değişikliği
        if (oldOffer.Width != newOffer.Width)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - En",
                oldOffer.Width.ToString(),
                newOffer.Width.ToString(),
                "Güncellendi"
            );
        }

        // Boy değişikliği
        if (oldOffer.Height != newOffer.Height)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Boy",
                oldOffer.Height.ToString(),
                newOffer.Height.ToString(),
                "Güncellendi"
            );
        }

        // Kağıt Bilgisi değişikliği
        if (oldOffer.PaperInfoId != newOffer.PaperInfoId)
        {
            var oldPaper = _context.PaperInfos.Find(oldOffer.PaperInfoId)?.Name ?? "Bilinmeyen";
            var newPaper = _context.PaperInfos.Find(newOffer.PaperInfoId)?.Name ?? "Bilinmeyen";

            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Kağıt Bilgisi",
                oldPaper,
                newPaper,
                "Güncellendi"
            );
        }

        // Tutkal Bilgisi değişikliği
        if (oldOffer.AdhesiveInfoId != newOffer.AdhesiveInfoId)
        {
            var oldAdhesive =
                _context.AdhesiveInfos.Find(oldOffer.AdhesiveInfoId)?.Name ?? "Bilinmeyen";
            var newAdhesive =
                _context.AdhesiveInfos.Find(newOffer.AdhesiveInfoId)?.Name ?? "Bilinmeyen";

            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Tutkal Bilgisi",
                oldAdhesive,
                newAdhesive,
                "Güncellendi"
            );
        }

        // Teslim Şekli değişikliği
        if (oldOffer.DeliveryMethodId != newOffer.DeliveryMethodId)
        {
            var oldDeliveryMethod =
                _context.DeliveryMethods.Find(oldOffer.DeliveryMethodId)?.Name ?? "Bilinmeyen";
            var newDeliveryMethod =
                _context.DeliveryMethods.Find(newOffer.DeliveryMethodId)?.Name ?? "Bilinmeyen";

            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Teslim Şekli",
                oldDeliveryMethod,
                newDeliveryMethod,
                "Güncellendi"
            );
        }

        // Sipariş Birimi değişikliği
        if (oldOffer.OrderMethodId != newOffer.OrderMethodId)
        {
            var oldOrderMethod =
                _context.OrderMethods.Find(oldOffer.OrderMethodId)?.Name ?? "Bilinmeyen";
            var newOrderMethod =
                _context.OrderMethods.Find(newOffer.OrderMethodId)?.Name ?? "Bilinmeyen";

            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Sipariş Birimi",
                oldOrderMethod,
                newOrderMethod,
                "Güncellendi"
            );
        }

        // Renk Sayısı değişikliği
        if (oldOffer.NumberOfColors != newOffer.NumberOfColors)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Renk Sayısı",
                oldOffer.NumberOfColors?.ToString() ?? "Bilinmeyen",
                newOffer.NumberOfColors?.ToString() ?? "Bilinmeyen",
                "Güncellendi"
            );
        }

        // Ödeme Vadesi değişikliği
        if (oldOffer.PaymentTerm != newOffer.PaymentTerm)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Ödeme Vadesi",
                oldOffer.PaymentTerm?.ToString() ?? "Bilinmeyen",
                newOffer.PaymentTerm?.ToString() ?? "Bilinmeyen",
                "Güncellendi"
            );
        }

        // Baskılı / Baskısız değişikliği
        if (oldOffer.IsPrinted != newOffer.IsPrinted)
        {
            var oldPrintStatus = oldOffer.IsPrinted ? "Baskılı" : "Baskısız";
            var newPrintStatus = newOffer.IsPrinted ? "Baskılı" : "Baskısız";

            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Baskılı/Baskısız",
                oldPrintStatus,
                newPrintStatus,
                "Güncellendi"
            );
        }
        // Ödeme Yöntemi değişikliği
        if (oldOffer.PaymentMethod != newOffer.PaymentMethod)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - Ödeme Yöntemi",
                oldOffer.PaymentMethod,
                newOffer.PaymentMethod,
                "Güncellendi"
            );
        }

        var oldProcessingList = oldOffer.AdditionalProcessing?.Split(',') ?? Array.Empty<string>();
        var newProcessingList = newOffer.AdditionalProcessing?.Split(',') ?? Array.Empty<string>();

        // AdditionalProcessing isimlerini ID ile eşle
        var additionalProcessingNames = _context.AdditionalProcessings.ToDictionary(
            ap => ap.Id,
            ap => ap.Name
        );

        // Eski ve yeni AdditionalProcessing listelerini isimleriyle alın
        var oldProcessingNames = oldProcessingList
            .Select(id => additionalProcessingNames.GetValueOrDefault(int.Parse(id), "Bilinmiyor"))
            .ToList();
        var newProcessingNames = newProcessingList
            .Select(id => additionalProcessingNames.GetValueOrDefault(int.Parse(id), "Bilinmiyor"))
            .ToList();

        // Eski ve yeni değerleri loglama
        var oldText = oldProcessingNames.Any()
            ? $"{string.Join(", ", oldProcessingNames)}"
            : "İlave İşlem Yok";
        var newText = newProcessingNames.Any()
            ? $"{string.Join(", ", newProcessingNames)}"
            : "İlave İşlem Yok";

        // Sadece eski ve yeni metinler farklıysa logla
        if (oldText != newText)
        {
            LogChange(
                "Customers",
                customerId,
                $"{offerIdentifier} - İlave İşlem",
                oldText,
                newText,
                "Güncellendi"
            );
        }
    }

    private string GetProcessingNames(Offer offer)
    {
        var processingNames = _context
            .OfferAdditionalProcessings.Where(o => o.OfferId == offer.Id)
            .Select(o => o.AdditionalProcessing.Name)
            .ToList();

        return processingNames.Any() ? string.Join(", ", processingNames) : "Yok";
    }

    [HttpPost]
    public IActionResult DownloadMultipleOffersPdf([FromBody] OfferIdsDto offerIdsDto)
    {
        if (offerIdsDto == null || offerIdsDto.OfferIds == null || !offerIdsDto.OfferIds.Any())
        {
            return BadRequest("Teklif ID'leri bulunamadı.");
        }

        _logger.LogInformation(
            "Gelen Teklif ID'leri:{OfferIds}",
            string.Join(", ", offerIdsDto.OfferIds)
        );

        try
        {
            // Seçilen teklifleri ve müşteri bilgilerini alıyoruz
            var selectedOffers = _context
                .Offers.Include(o => o.Customer)
                .Include(o => o.PaperInfo)
                .Include(o => o.AdhesiveInfo)
                .Include(o => o.DeliveryMethod)
                .Include(o => o.OrderMethod)
                .Where(o => offerIdsDto.OfferIds.Contains(o.Id))
                .ToList();

            if (!selectedOffers.Any())
            {
                return NotFound("Seçilen teklifler bulunamadı.");
            }

            _logger.LogInformation("Seçilen teklifler: {Count}", selectedOffers.Count);

            // Kullanıcı bilgilerini al
            var userIds = selectedOffers.Select(o => o.Customer.CreatedById).Distinct().ToList();
            var users = _context.Users.Where(u => userIds.Contains(u.Id)).ToList();

            // Her teklifi işleyerek PDF için kullanıcı bilgilerini ekliyoruz
            foreach (var offer in selectedOffers)
            {
                var user = users.FirstOrDefault(u => u.Id == offer.Customer.CreatedById);
                if (user == null)
                {
                    _logger.LogWarning(
                        "User with ID {CreatedById} not found for offer {OfferId}.",
                        offer.Customer.CreatedById,
                        offer.Id
                    );
                    return NotFound($"User not found for offer {offer.Id}");
                }

                // PDF oluşturma işlemi burada gerçekleşiyor
                var pdfBytes = _pdfService.GenerateMultipleOffersPdf(
                    selectedOffers,
                    user?.PhoneNumber,
                    user?.Email
                );

                return File(pdfBytes, "application/pdf", "Byb.pdf");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF oluşturma hatası: {ErrorMessage}", ex.Message);
            return StatusCode(500, $"Sunucu hatası: {ex.Message}");
        }

        return BadRequest("Beklenmedik bir hata oluştu.");
    }

    public IActionResult DownloadPdf(int offerId)
    {
        try
        {
            // Teklif verilerini ve müşteri bilgilerini al
            var offer = _context
                .Offers.Include(o => o.Customer)
                .Include(o => o.PaperInfo)
                .Include(o => o.AdhesiveInfo)
                .Include(o => o.DeliveryMethod)
                .Include(o => o.OrderMethod)
                .FirstOrDefault(o => o.Id == offerId);

            if (offer == null)
            {
                _logger.LogWarning("Offer with ID {OfferId} not found.", offerId);
                return NotFound("Offer not found.");
            }

            // Kullanıcının bilgilerini almak için CreatedById kullan
            var user = _context.Users.FirstOrDefault(u => u.Id == offer.Customer.CreatedById);

            if (user == null)
            {
                _logger.LogWarning(
                    "User with ID {CreatedById} not found.",
                    offer.Customer.CreatedById
                );
                return NotFound("User not found.");
            }

            var pdfService = new PdfService
            {
                PaymentTerm = (offer.PaymentTerm?.ToString() ?? "BELİRTİLMEDİ").ToUpper(), // PaymentTerm'i büyük harfe çeviriyoruz
                PaymentMethod = (offer.PaymentMethod ?? "BELİRTİLMEDİ").ToUpper(), // PaymentMethod'u büyük harfe çeviriyoruz
            };

            // PDF oluştur
            var pdfBytes = pdfService.GeneratePdf(
                offer.Customer.Name, // Müşteri ismini kullan
                offer.Id.ToString(), // Teklif ID
                DateTime.Now, // Tarih
                offer.Currency ?? "N/A", // Döviz bilgisi
                offer.OrderQuantity, // Sipariş miktarı
                offer.OrderMethod?.Name ?? "Belirtilmedi", // Sipariş yöntemi
                offer.Customer.CreatedBy, // Teklif oluşturucusu
                user?.PhoneNumber ?? "N/A", // Kullanıcı telefon bilgisi
                user?.Email ?? "N/A", // Kullanıcı email bilgisi
                offer.PaymentMethod,
                offer.PaymentTerm.ToString(),
                offer
            ); // Teklif detayları

            return File(pdfBytes, "application/pdf", $"Teklif({offer.Id}).pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while processing the offer with ID {OfferId}.",
                offerId
            );
            return StatusCode(500, "Internal server error. Please try again later.");
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteOffer(int id)
    {
        try
        {
            // Teklif ve ilgili müşteri bilgilerini yükle
            var offer = await _context
                .Offers.Include(o => o.Customer) // Müşteri bilgisi dahil et
                .FirstOrDefaultAsync(o => o.Id == id);

            if (offer == null)
            {
                return Json(new { success = false, message = "Offer not found." });
            }

            // Müşteri bilgisi varsa adını al, yoksa "Bilinmeyen Müşteri"
            var customerName = offer.Customer != null ? offer.Customer.Name : "Bilinmeyen Müşteri";

            // Log kaydı için teklif bilgilerini hazırla
            var offerInfo = $"Teklif ID:{offer.Id}, Ürün: {offer.ProductName ?? "Bilinmiyor"}";

            // Teklif resmi varsa sil
            if (!string.IsNullOrEmpty(offer.OfferPicture))
            {
                var filePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    offer.OfferPicture.TrimStart('/')
                );
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            LogChange(
                "Customers",
                offer.Customer?.Id ?? 0, // Null kontrolü ile ID
                $"Teklif Silindi - {offer.Id}", // Teklif kimliği logda gösteriliyor
                $"Ürün Adı: {offer.ProductName ?? "Bilinmiyor"}",
                "",
                "Silindi"
            );

            _context.Offers.Remove(offer);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            // Hata durumunda konsola yazdır
            Console.WriteLine($"Exception: {ex.Message}");
            return Json(
                new
                {
                    success = false,
                    message = "An unexpected error occurred.",
                    error = ex.Message,
                }
            );
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetOtherCostById(int id)
    {
        var OtherCost = _context.OtherCosts.Find(id);
        if (OtherCost == null)
        {
            return NotFound();
        }
        return Json(OtherCost);
    }

    [HttpGet]
    public async Task<IActionResult> GetAdditionalProcessingsByIds(string ids)
    {
        if (string.IsNullOrEmpty(ids))
        {
            // IDs boşsa boş bir liste döndürelim
            return Json(new List<string>());
        }

        // ID'leri virgülle ayrılmış string'den int listesine çeviriyoruz
        var idList = ids.Split(',').Select(id => int.Parse(id.Trim())).ToList();

        // Bu ID'lere karşılık gelen AdditionalProcessing kayıtlarını alıyoruz
        var additionalProcessings = await _context
            .AdditionalProcessings.Where(ap => idList.Contains(ap.Id))
            .ToListAsync();

        // Sadece Name alanlarını JSON olarak döndürüyoruz
        var names = additionalProcessings.Select(ap => ap.Name).ToList();

        return Json(names); // JSON formatında isimleri döndürüyoruz
    }

    [HttpGet]
    public async Task<IActionResult> GetAdditionalProcessingById(int id)
    {
        var additionalProcessing = _context.AdditionalProcessings.Find(id);
        if (additionalProcessing == null)
        {
            return NotFound();
        }
        return Json(additionalProcessing);
    }

    [HttpGet]
    public async Task<IActionResult> GetMachineById(int id)
    {
        var machine = _context.Machines.Find(id);
        if (machine == null)
        {
            return NotFound();
        }
        return Json(machine);
    }

    [HttpGet]
    public async Task<IActionResult> GetTermById(int id)
    {
        var term = _context.Terms.Find(id);
        if (term == null)
        {
            return NotFound();
        }
        return Json(term);
    }

    [HttpGet]
    public async Task<IActionResult> GetCombinationById(int id)
    {
        var combinationPaperAdhesive = _context.CombinationPaperAdhesives.Find(id);
        if (combinationPaperAdhesive == null)
        {
            return NotFound();
        }
        return Json(combinationPaperAdhesive);
    }

    [HttpPost]
    public IActionResult UpdateCombination(CombinationPaperAdhesive combinationPaperAdhesive)
    {
        if (ModelState.IsValid)
        {
            var existingCombination = _context.CombinationPaperAdhesives.FirstOrDefault(c =>
                c.Id == combinationPaperAdhesive.Id
            );
            if (existingCombination == null)
            {
                return Json(new { success = false, message = "Kombinasyon bulunamadı." });
            }

            // Fiyat değişmişse geçmişe ekleyelim
            if (existingCombination.Cost != combinationPaperAdhesive.Cost)
            {
                var priceHistory = new CombinationPriceHistory
                {
                    CombinationId = existingCombination.Id,
                    OldCost = existingCombination.Cost,
                    UpdatedAt = DateTime.UtcNow.AddHours(3), // Türkiye saati
                };
                _context.CombinationPriceHistories.Add(priceHistory);
            }

            // Mevcut kaydı güncelle
            existingCombination.Name = combinationPaperAdhesive.Name;
            existingCombination.Cost = combinationPaperAdhesive.Cost;
            existingCombination.UpdateTime = DateTime.UtcNow.AddHours(3); // Güncelleme zamanı otomatik ayarlanıyor

            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateAdditionalProcessing(AdditionalProcessing additionalProcessing)
    {
        ModelState.Remove("OfferAdditionalProcessings");
        if (ModelState.IsValid)
        {
            var existingProcessing = _context.AdditionalProcessings.FirstOrDefault(a =>
                a.Id == additionalProcessing.Id
            );
            if (existingProcessing == null)
            {
                return Json(new { success = false, message = "İşlem bulunamadı." });
            }

            // Eğer fiyat değişmişse geçmişe ekleyelim
            if (existingProcessing.Cost != additionalProcessing.Cost)
            {
                var costHistory = new AdditionalProcessingCostHistory
                {
                    AdditionalProcessingId = existingProcessing.Id,
                    OldCost = existingProcessing.Cost,
                    UpdatedAt = DateTime.UtcNow.AddHours(3), // Türkiye saati
                };
                _context.AdditionalProcessingCostHistories.Add(costHistory);
            }

            // Mevcut kaydı güncelle
            existingProcessing.Name = additionalProcessing.Name;
            existingProcessing.Cost = additionalProcessing.Cost;
            existingProcessing.UpdateTime = DateTime.UtcNow.AddHours(3);

            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateOtherCost(OtherCost otherCost)
    {
        ModelState.Remove("OfferAdditionalProcessings");
        if (ModelState.IsValid)
        {
            var existingProcessing = _context.OtherCosts.FirstOrDefault(a => a.Id == otherCost.Id);
            if (existingProcessing == null)
            {
                return Json(new { success = false, message = "İşlem bulunamadı." });
            }

            // Eğer fiyat değişmişse geçmişe ekleyelim
            if (existingProcessing.Cost != otherCost.Cost)
            {
                var costHistory = new OtherCostsHistory
                {
                    OtherCostId = existingProcessing.Id,
                    OldCost = existingProcessing.Cost,
                    UpdatedAt = DateTime.UtcNow.AddHours(3), // Türkiye saati
                };
                _context.OtherCostsHistories.Add(costHistory);
            }

            // Mevcut kaydı güncelle
            existingProcessing.Name = otherCost.Name;
            existingProcessing.Amount = otherCost.Amount;
            existingProcessing.Cost = otherCost.Cost;
            existingProcessing.UpdateTime = DateTime.UtcNow.AddHours(3);

            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult CreateOtherCost(OtherCost otherCost)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Diğer Maliyet ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
            TempData["SwalType"] = "error"; // Hata olduğunda 'error' olarak ayarlanmalı

            return RedirectToAction("Costs", "Customer");
        }

        try
        {
            _context.OtherCosts.Add(otherCost);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Diğer Maliyet başarıyla eklendi!";
            TempData["SwalType"] = "success"; // Başarı mesajı yeşil tik ile gösterilir
        }
        catch (Exception ex)
        {
            TempData["SwalMessage"] = "Diğer Maliyet hata oluştu: " + ex.Message;
            TempData["SwalType"] = "error"; // Hata mesajı kırmızı ikon ile gösterilir
        }

        return RedirectToAction("Costs", "Customer");
    }

    [HttpPost]
    public IActionResult CreateTerm(Term term)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Ödeme koşulu ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
            TempData["SwalType"] = "error";

            return RedirectToAction("Costs", "Customer");
        }

        try
        {
            _context.Terms.Add(term);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Ödeme koşulu başarıyla eklendi!";
            TempData["SwalType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["SwalMessage"] = "Ödeme koşulu eklenirken hata oluştu: " + ex.Message;
            TempData["SwalType"] = "error";
        }

        return RedirectToAction("Costs", "Customer");
    }

    [HttpPost]
    public IActionResult CreateMachine(Machine machine)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] =
                "Makina ekleme işlemi sırasında bir hata oluştu: " + errorMessage;
            TempData["SwalType"] = "error"; // Hata olduğunda 'error' olarak ayarlanmalı

            return RedirectToAction("Costs", "Customer");
        }

        try
        {
            _context.Machines.Add(machine);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Makina başarıyla eklendi!";
            TempData["SwalType"] = "success"; // Başarı mesajı yeşil tik ile gösterilir
        }
        catch (Exception ex)
        {
            TempData["SwalMessage"] = "Makina eklenirken hata oluştu: " + ex.Message;
            TempData["SwalType"] = "error"; // Hata mesajı kırmızı ikon ile gösterilir
        }

        return RedirectToAction("Costs", "Customer");
    }

    [HttpPost]
    public IActionResult DeleteAdditionalProcessing(int id)
    {
        var additionalProcessing = _context.AdditionalProcessings.Find(id);
        if (additionalProcessing != null)
        {
            _context.AdditionalProcessings.Remove(additionalProcessing);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteTerm(int id)
    {
        var term = _context.Terms.Find(id);
        if (term != null)
        {
            _context.Terms.Remove(term);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteRecipe(int id)
    {
        var recipe = _context.Recipes.Find(id);
        if (recipe != null)
        {
            _context.Recipes.Remove(recipe);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteMachine(int id)
    {
        var machine = _context.Machines.Find(id);
        if (machine != null)
        {
            _context.Machines.Remove(machine);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult DeleteCombination(int id)
    {
        var combinationPaperAdhesive = _context.CombinationPaperAdhesives.Find(id);
        if (combinationPaperAdhesive != null)
        {
            _context.CombinationPaperAdhesives.Remove(combinationPaperAdhesive);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateMachine(Machine machine)
    {
        if (ModelState.IsValid)
        {
            var existingMachine = _context.Machines.FirstOrDefault(m => m.Id == machine.Id);
            if (existingMachine == null)
            {
                return Json(new { success = false, message = "Makine bulunamadı." });
            }

            // Fiyat değişmişse geçmişe ekleyelim
            if (existingMachine.Cost != machine.Cost)
            {
                var priceHistory = new MachinePriceHistory
                {
                    MachineId = existingMachine.Id,
                    OldCost = existingMachine.Cost,
                    UpdatedAt = DateTime.UtcNow.AddHours(3), // Türkiye saati
                };
                _context.MachinePriceHistories.Add(priceHistory);
            }

            // Mevcut kaydı güncelle
            existingMachine.Name = machine.Name;
            existingMachine.Cost = machine.Cost;
            existingMachine.UpdateTime = DateTime.UtcNow.AddHours(3);

            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateTerm(Term term)
    {
        if (ModelState.IsValid)
        {
            var existingTerm = _context.Terms.FirstOrDefault(t => t.Id == term.Id);
            if (existingTerm == null)
            {
                return Json(new { success = false, message = "Ödeme koşulu bulunamadı." });
            }

            // Yüzdelik değişmişse geçmişe ekleyelim
            if (existingTerm.Percent != term.Percent)
            {
                var priceHistory = new TermPriceHistory
                {
                    TermId = existingTerm.Id,
                    OldCost = existingTerm.Percent,
                    UpdatedAt = DateTime.UtcNow.AddHours(3), // Türkiye saati
                };
                _context.TermPriceHistories.Add(priceHistory);
            }

            // Mevcut kaydı güncelle
            existingTerm.Name = term.Name;
            existingTerm.Percent = term.Percent;
            existingTerm.UpdateTime = DateTime.UtcNow.AddHours(3);

            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult CreateCombination(CombinationPaperAdhesive combinationPaperAdhesive)
    {
        ModelState.Remove("UpdateTime"); // UpdateTime modeli doğrulama sırasında hata vermesin

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors);
            string errorMessage = string.Join(", ", errors.Select(e => e.ErrorMessage));

            TempData["SwalMessage"] = "Kombinasyon ekleme sırasında hata oluştu: " + errorMessage;
            TempData["SwalType"] = "error"; // Hata mesajı

            return RedirectToAction("Costs", "Customer");
        }

        try
        {
            combinationPaperAdhesive.UpdateTime = DateTime.UtcNow.AddHours(3); // Türkiye saatine göre ayarla
            _context.CombinationPaperAdhesives.Add(combinationPaperAdhesive);
            _context.SaveChanges();

            TempData["SwalMessage"] = "Kombinasyon başarıyla eklendi!";
            TempData["SwalType"] = "success"; // Başarı mesajı
        }
        catch (Exception ex)
        {
            TempData["SwalMessage"] = "Kombinasyon eklenirken hata oluştu: " + ex.Message;
            TempData["SwalType"] = "error"; // Hata mesajı
        }

        return RedirectToAction("Costs", "Customer");
    }

    [HttpPost]
    public IActionResult CreateMailInfo(MailInfo mailInfo)
    {
        if (ModelState.IsValid)
        {
            _context.MailInfos.Add(mailInfo);
            _context.SaveChanges();
            TempData["SwalMessage"] = "Mail bilgisi başarıyla eklendi!";
        }
        else
        {
            TempData["SwalMessage"] = "Mail bilgisi ekleme işlemi sırasında bir hata oluştu.";
        }
        return RedirectToAction("Definition", "Customer");
    }

    [HttpPost]
    public IActionResult DeleteMailInfo(int id)
    {
        var mailInfo = _context.MailInfos.Find(id);
        if (mailInfo != null)
        {
            _context.MailInfos.Remove(mailInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Silme işlemi başarısız oldu." });
    }

    [HttpPost]
    public IActionResult UpdateMailInfo(MailInfo mailInfo)
    {
        if (ModelState.IsValid)
        {
            _context.MailInfos.Update(mailInfo);
            _context.SaveChanges();
            return Json(new { success = true });
        }
        return Json(new { success = false, message = "Güncelleme işlemi başarısız oldu." });
    }

    // Action to handle form submission from the PaperInfo modal


    // Action to handle form submission from the DeliveryMethod modal

    // Action to handle form submission from the OrderMethod modal


    [HttpGet]
    public IActionResult GetMailInfoById(int id)
    {
        var mailInfo = _context.MailInfos.Find(id);
        if (mailInfo == null)
        {
            return NotFound();
        }
        return Json(mailInfo);
    }

    [HttpGet]
    public async Task<IActionResult> GetOfferById(int id)
    {
        // Tek bir Offer verisini çekiyoruz ve AdditionalProcessing verilerini ekliyoruz
        var offer = await _context
            .Offers.Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.Width,
                o.Height,
                o.PaperInfo,
                o.Price,
                o.Currency,
                o.AdhesiveInfo,
                o.DeliveryMethod,
                o.PaymentMethod,
                o.IsPrinted,
                o.Description,
                o.NumberOfColors,
                o.OrderMethod,
                o.OrderQuantity,
                o.PaymentTerm,
                AdditionalProcessings = _context
                    .AdditionalProcessings // AdditionalProcessing verisini doğrudan çekiyoruz
                    .Where(ap => o.AdditionalProcessing.Contains(ap.Id.ToString())) // ID'leri karşılaştırıyoruz
                    .Select(ap => new { ap.Id, ap.Name }) // ID ve Name bilgilerini çekiyoruz
                    .ToList(),
            })
            .FirstOrDefaultAsync();

        if (offer == null)
        {
            return Json(new { success = false, message = "Offer not found." });
        }

        return Json(offer);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteSectorByName(string name)
    {
        var sector = await _context.Sectors.FirstOrDefaultAsync(s => s.Name == name);
        if (sector != null)
        {
            // Sektöre ait müşterilerin varlığını kontrol et
            var hasCustomers = await _context.Customers.AnyAsync(c => c.Sector == name);
            if (hasCustomers)
            {
                return Json(new { success = false, message = "Bu sektöre ait müşteriler mevcut." });
            }

            try
            {
                // Log ekle (Kayıt ID'si yerine sektör adı kullanıyoruz)
                LogChange("", 0, "Sectors", sector.Name, "", "Silindi");

                _context.Sectors.Remove(sector);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
            }
        }
        return Json(new { success = false, message = "Sektör bulunamadı" });
    }

    // [DynamicAuthorize("DeleteCustomer")]
    // [HttpPost]
    // public IActionResult DeleteSelectedCustomers(List<int> ids)
    // {
    //     if (ids == null || !ids.Any())
    //     {
    //         return Json(new { success = false, message = "Silinecek müşteri bulunamadı." });
    //     }

    //     foreach (var id in ids)
    //     {
    //         var customer = _context.Customers
    //                                .Include(c => c.Offers) // Müşterinin tekliflerini de dahil et
    //                                .FirstOrDefault(c => c.Id == id);
    //         if (customer != null)
    //         {
    //             // Müşterinin tüm tekliflerini sil
    //             _context.Offers.RemoveRange(customer.Offers);

    //             // Ardından müşteriyi sil
    //             _context.Customers.Remove(customer);
    //         }
    //     }

    //     _context.SaveChanges();
    //     return Json(new { success = true, message = "Seçilen müşteriler ve ilgili teklifleri başarıyla silindi." });
    // }
    // [DynamicAuthorize("DeleteCustomer")]
    // [HttpPost]
    // public async Task<IActionResult> DeleteCustomer(int id)
    // {
    //     var customer = await _context.Customers
    //         .Include(c => c.Contacts)
    //         .Include(c => c.Offers) // Offerlar dahil ediliyor
    //         .FirstOrDefaultAsync(c => c.Id == id);

    //     if (customer == null)
    //     {
    //         return Json(new { success = false, message = "Müşteri bulunamadı." });
    //     }

    //     // Müşteriye ait offerları siliyoruz
    //     if (customer.Offers != null && customer.Offers.Any())
    //     {
    //         _context.Offers.RemoveRange(customer.Offers);
    //     }

    //     // Müşteriye ait kontakları siliyoruz
    //     if (customer.Contacts != null && customer.Contacts.Any())
    //     {
    //         _context.Contacts.RemoveRange(customer.Contacts);
    //     }

    //     // Müşteriyi siliyoruz
    //     _context.Customers.Remove(customer);
    //     await _context.SaveChangesAsync();

    //     return Json(new { success = true });
    // }

    [HttpGet]
    public async Task<IActionResult> GetCustomerCity(int customerId)
    {
        var customerCity = await _context
            .Customers.Where(c => c.Id == customerId)
            .Select(c => new { c.City })
            .FirstOrDefaultAsync();

        if (customerCity == null)
        {
            return NotFound();
        }

        return Json(customerCity);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaperInfos()
    {
        var paperInfos = await _context.PaperInfos.Select(p => new { p.Id, p.Name }).ToListAsync();

        return Json(paperInfos);
    }

    public async Task<IActionResult> GetPotentialCustomerInfos()
    {
        // Kullanıcının ID'sini al
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, out int userId))
        {
            return View("Error", new ErrorViewModel { RequestId = "User ID conversion failed." });
        }

        IEnumerable<object> customers;

        // Eğer kullanıcı Yönetici, GENEL MÜDÜR ya da Denetlemeci ise tüm potansiyel müşterileri getir
        if (
            User.IsInRole("Yönetici")
            || User.IsInRole("GENEL MÜDÜR")
            || User.IsInRole("Denetlemeci")
        )
        {
            customers = await _context
                .Customers.Where(p => p.IsPotential == true) // Sadece IsPotential = 1 olan kayıtlar
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();
        }
        else
        {
            // Yönetici değilse sadece kendi sahip olduğu potansiyel müşterileri getir
            customers = await _context
                .Customers.Where(c => c.CreatedById == userId && c.IsPotential == true) // Kullanıcının oluşturduğu ve IsPotential = 1 olan kayıtlar
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();
        }

        return Json(new { success = true, data = customers }); // JSON olarak döndürüyoruz
    }

    public async Task<IActionResult> GetCustomerInfos()
    {
        // Kullanıcının ID'sini al
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, out int userId))
        {
            return View("Error", new ErrorViewModel { RequestId = "User ID conversion failed." });
        }

        IEnumerable<object> customers;

        // Eğer kullanıcı Yönetici ya da GENEL MÜDÜR ise tüm müşterileri getir
        if (
            User.IsInRole("Yönetici")
            || User.IsInRole("GENEL MÜDÜR")
            || User.IsInRole("Denetlemeci")
        )
        {
            customers = await _context.Customers.Select(p => new { p.Id, p.Name }).ToListAsync();
        }
        else
        {
            // Yönetici değilse sadece kendi sahip olduğu müşterileri getir
            customers = await _context
                .Customers.Where(c => c.CreatedById == userId) // Kullanıcının oluşturduğu müşterileri filtrele
                .Select(p => new { p.Id, p.Name })
                .ToListAsync();
        }

        return Json(customers); // Anonim türü doğrudan JSON olarak döndürüyoruz
    }

    [HttpGet]
    public async Task<IActionResult> GetAdditionalProcessings()
    {
        var AdditionalProcessing = await _context
            .AdditionalProcessings.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(AdditionalProcessing);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateDesignerInfo(Recipe model, List<int> SelectedMachineIds)
    {
        var recipe = await _context
            .Recipes.Include(r => r.RecipeMachines)
            .FirstOrDefaultAsync(r => r.Id == model.Id);

        if (recipe == null)
            return Json(new { success = false, message = "Reçete bulunamadı." });

        // 1. Kullanılan makineleri güncelle
        _context.RecipeMachines.RemoveRange(recipe.RecipeMachines);

        if (SelectedMachineIds != null && SelectedMachineIds.Any())
        {
            foreach (var machineId in SelectedMachineIds)
            {
                recipe.RecipeMachines.Add(
                    new RecipeMachine { RecipeId = recipe.Id, MachineId = machineId }
                );
            }
        }

        // 2. Diğer alanları güncelle
        recipe.DesignerNote = model.DesignerNote;
        recipe.ColorCyan = model.ColorCyan;
        recipe.ColorMagenta = model.ColorMagenta;
        recipe.ColorYellow = model.ColorYellow;
        recipe.ColorKey = model.ColorKey;
        recipe.ExtraColor1 = model.ExtraColor1;
        recipe.ExtraColor2 = model.ExtraColor2;
        recipe.ExtraColor3 = model.ExtraColor3;
        recipe.ExtraColor4 = model.ExtraColor4;
        recipe.ExtraColor5 = model.ExtraColor5;
        recipe.DieCutPlateTypeId = model.DieCutPlateTypeId;
        recipe.PlateCount = model.PlateCount;
        recipe.PlateNumber = model.PlateNumber;
        recipe.PaperWidth = model.PaperWidth;
        recipe.SerialNumber = model.SerialNumber;
        recipe.Format = model.Format;
        recipe.MountQtyYY = model.MountQtyYY;
        recipe.MountQtyAA = model.MountQtyAA;
        recipe.KnifeCode = model.KnifeCode;
        recipe.KnifeTypeId = model.KnifeTypeId;
        recipe.KnifeAA = model.KnifeAA;
        recipe.KnifeYY = model.KnifeYY;
        recipe.KnifeYYAB = model.KnifeYYAB;
        recipe.Radius = model.Radius;
        recipe.ZetDiameter = model.ZetDiameter;
        recipe.NoteToDesigner = model.NoteToDesigner;
        try
        {
            int userId = GetCurrentUserId();

            var loggedInUser = await _context
                .Users.Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (
                loggedInUser != null
                && loggedInUser.UserRoles.Any(ur => ur.Role.ToLower() == "grafiker")
            )
            {
                recipe.DesignerId = loggedInUser.Id;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DesignerId atama hatası: {ex.Message}");
        }

        await _context.SaveChangesAsync();

        return Json(new { success = true, id = recipe.Id });
    }

    [HttpPost]
    public async Task<IActionResult> CopyRecipe(int id)
    {
        var originalRecipe = await _context
            .Recipes.Include(r => r.RecipeAdditionalProcessings)
            .Include(r => r.RecipeMachines)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (originalRecipe == null)
            return Json(new { success = false, message = "Orijinal reçete bulunamadı." });

        var newRecipe = new Recipe
        {
            CustomerId = originalRecipe.CustomerId,
            RecipeName = originalRecipe.RecipeName,
            LocationId = originalRecipe.LocationId,
            Width = originalRecipe.Width,
            Height = originalRecipe.Height,
            OuterDiameter = originalRecipe.OuterDiameter,
            LabelPerWrap = originalRecipe.LabelPerWrap,
            CustomerCode = originalRecipe.CustomerCode,
            Quantity = originalRecipe.Quantity,
            NoteToDesigner = originalRecipe.NoteToDesigner,
            NoteForProduction = originalRecipe.NoteForProduction,
            PaperTypeId = originalRecipe.PaperTypeId,
            PaperAdhesionTypeId = originalRecipe.PaperAdhesionTypeId,
            CustomerAdhesionTypeId = originalRecipe.CustomerAdhesionTypeId,
            CoreDiameterId = originalRecipe.CoreDiameterId,
            PackageTypeId = originalRecipe.PackageTypeId,
            UnitId = originalRecipe.UnitId,
            AdditionalProcessing = originalRecipe.AdditionalProcessing,
            WindingDirectionType = originalRecipe.WindingDirectionType,
            CoreLengthId = originalRecipe.CoreLengthId,
            PaperDetailId = originalRecipe.PaperDetailId,
            ShipmentMethodId = originalRecipe.ShipmentMethodId,
            DesignerId = originalRecipe.DesignerId,
            ArchiveStatus = 0,
            CreatedAt = DateTime.Now,
            CreatedById = originalRecipe.CreatedById,
            CurrentStatus = 1,
            DesignerNote = originalRecipe.DesignerNote,
            ColorCyan = originalRecipe.ColorCyan,
            ColorMagenta = originalRecipe.ColorMagenta,
            ColorYellow = originalRecipe.ColorYellow,
            ColorKey = originalRecipe.ColorKey,
            ExtraColor1 = originalRecipe.ExtraColor1,
            ExtraColor2 = originalRecipe.ExtraColor2,
            ExtraColor3 = originalRecipe.ExtraColor3,
            ExtraColor4 = originalRecipe.ExtraColor4,
            ExtraColor5 = originalRecipe.ExtraColor5,
            DieCutPlateTypeId = originalRecipe.DieCutPlateTypeId,
            PlateCount = originalRecipe.PlateCount,
            PlateNumber = originalRecipe.PlateNumber,
            PaperWidth = originalRecipe.PaperWidth,
            Format = originalRecipe.Format,
            MountQtyYY = originalRecipe.MountQtyYY,
            MountQtyAA = originalRecipe.MountQtyAA,
            KnifeTypeId = originalRecipe.KnifeTypeId,
            KnifeAA = originalRecipe.KnifeAA,
            KnifeYY = originalRecipe.KnifeYY,
            KnifeYYAB = originalRecipe.KnifeYYAB,
            Radius = originalRecipe.Radius,
            ZetDiameter = originalRecipe.ZetDiameter,
        };

        _context.Recipes.Add(newRecipe);
        await _context.SaveChangesAsync();

        newRecipe.RecipeCode = "R" + newRecipe.Id.ToString("D5");
        _context.Update(newRecipe);

        foreach (var rap in originalRecipe.RecipeAdditionalProcessings)
        {
            newRecipe.RecipeAdditionalProcessings.Add(
                new RecipeAdditionalProcessing
                {
                    RecipeId = newRecipe.Id,
                    AdditionalProcessingId = rap.AdditionalProcessingId,
                }
            );
        }

        foreach (var rm in originalRecipe.RecipeMachines)
        {
            newRecipe.RecipeMachines.Add(
                new RecipeMachine { RecipeId = newRecipe.Id, MachineId = rm.MachineId }
            );
        }

        // Giriş yapan kullanıcı ID
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdString, out var userId);

        // ✅ Log – Yeni Reçeteye
        _context.RecipeLogs.Add(
            new RecipeLog
            {
                RecipeId = newRecipe.Id,
                FieldName = "Reçete Kopyalanarak Oluşturuldu",
                OldValue = $"Kaynak Reçete ID: {originalRecipe.Id}",
                NewValue = $"Yeni Reçete ID: {newRecipe.Id}",
                CreatedById = userId,
                RecordDate = DateTime.Now,
            }
        );

        // ✅ Log – Eski Reçeteye
        _context.RecipeLogs.Add(
            new RecipeLog
            {
                RecipeId = originalRecipe.Id,
                FieldName = "Reçete Kopyalandı",
                OldValue = $"Kaynak Reçete ID: {originalRecipe.Id}",
                NewValue = $"Yeni Reçete ID: {newRecipe.Id}",
                CreatedById = userId,
                RecordDate = DateTime.Now,
            }
        );

        await _context.SaveChangesAsync();

        return Json(
            new
            {
                success = true,
                message = "Reçete başarıyla kopyalandı.",
                recipeId = newRecipe.Id,
            }
        );
    }

    [HttpGet]
    public async Task<IActionResult> GetAdhesiveInfos()
    {
        var AdhesiveInfos = await _context
            .AdhesiveInfos.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(AdhesiveInfos);
    }

    [HttpGet]
    public async Task<IActionResult> GetKnives()
    {
        var Knives = await _context.Knifes.Select(p => new { p.Id, p.Name }).ToListAsync();

        return Json(Knives);
    }

    [HttpGet]
    public async Task<IActionResult> GetMachines()
    {
        var Machines = await _context.Machines.Select(p => new { p.Id, p.Name }).ToListAsync();

        return Json(Machines);
    }

    [HttpGet]
    public async Task<IActionResult> GetMoldCliches()
    {
        var MoldCliches = await _context
            .MoldCliches.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(MoldCliches);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaperDetails()
    {
        var PaperDetails = await _context
            .PaperDetails.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(PaperDetails);
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomerAdhesion()
    {
        var CustomerAdhesion = await _context
            .CustomerAdhesives.Select(p => new { p.Id, p.Definition })
            .ToListAsync();

        return Json(CustomerAdhesion);
    }

    [HttpGet]
    public async Task<IActionResult> GetPackage()
    {
        var Package = await _context
            .Packagings.Select(p => new { p.Id, p.Definition })
            .ToListAsync();

        return Json(Package);
    }

    [HttpGet]
    public async Task<IActionResult> GetCoreLenght()
    {
        var Core = await _context.Cores.Select(p => new { p.Id, p.Name }).ToListAsync();

        return Json(Core);
    }

    [HttpGet]
    public async Task<IActionResult> GetCoreDiameter()
    {
        var CoreDiameter = await _context
            .ChuckDiameters.Select(p => new { p.Id, p.Definition })
            .ToListAsync();

        return Json(CoreDiameter);
    }

    [HttpGet]
    public async Task<IActionResult> GetDeliveryMethods()
    {
        var DeliveryMethods = await _context
            .DeliveryMethods.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(DeliveryMethods);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrderMethods()
    {
        var OrderMethods = await _context
            .OrderMethods.Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return Json(OrderMethods);
    }

    [HttpPost("edit")]
    public IActionResult EditCustomer(CustomerViewModel model)
    {
        ModelState.Remove("Records");
        ModelState.Remove("CreatedBy");
        ModelState.Remove("Contacts");
        ModelState.Remove("Locations");

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return Json(
                new
                {
                    success = false,
                    message = "Invalid data",
                    errors = errors,
                }
            );
        }

        var customer = _context.Customers.Find(model.Id);
        if (customer == null)
        {
            return Json(new { success = false, message = "Customer not found" });
        }

        // Eski değerleri sakla
        var oldCustomer = new Customer
        {
            Name = customer.Name,
            Sector = customer.Sector,
            City = customer.City,
            District = customer.District,
        };

        // Yeni değerlerle güncelle
        customer.Name = model.Name.ToUpper(CultureInfo.CurrentCulture);
        customer.Sector = model.Sector.ToUpper(CultureInfo.CurrentCulture);
        customer.City = model.City;
        customer.District = model.District;

        try
        {
            // Değişiklikleri karşılaştır ve log ekle
            AddLogIfChanged("Name", oldCustomer.Name, model.Name, customer.Id);
            AddLogIfChanged("Sector", oldCustomer.Sector, model.Sector, customer.Id);
            AddLogIfChanged("City", oldCustomer.City, model.City, customer.Id);
            AddLogIfChanged("District", oldCustomer.District, model.District, customer.Id);

            // Değişiklikleri kaydet
            _context.SaveChanges();

            return Json(new { success = true, message = "Customer updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    // Değerler değişmişse log ekleme metodu
    private void AddLogIfChanged(string columnName, string oldValue, string newValue, int recordId)
    {
        // Null değerleri boş string olarak kabul et
        oldValue = oldValue ?? string.Empty;
        newValue = newValue ?? string.Empty;

        if (oldValue != newValue)
        {
            LogChange("Customers", recordId, columnName, oldValue, newValue, "Güncellendi");
        }
    }

    private void LogChange(
        string tableName,
        int recordId,
        string columnName,
        string oldValue,
        string newValue,
        string operationType,
        int? offerId = null
    )
    {
        // Kullanıcı ad ve soyadını Claims'den alıyoruz
        var firstName =
            User?.Claims.FirstOrDefault(c => c.Type == "FirstName")?.Value ?? "Bilinmeyen";
        var lastName = User?.Claims.FirstOrDefault(c => c.Type == "LastName")?.Value ?? "Kullanıcı";

        var fullName = $"{firstName} {lastName}"; // Ad ve soyad birleştiriliyor

        var log = new ChangeLog
        {
            TableName = tableName,
            RecordId = recordId,
            OfferId = offerId, // Teklif ID'si varsa burada kaydedilecek
            ColumnName = columnName,
            OldValue = oldValue,
            NewValue = newValue,
            OperationType = operationType,
            ChangedBy = fullName, // Ad Soyad bilgisi burada kullanılıyor
            ChangedAt = DateTime.Now,
        };

        _context.ChangeLogs.Add(log);
        _context.SaveChanges();
    }

    [HttpPost]
    public IActionResult AssignCustomer(int id, string createdBy, int createdById)
    {
        // Gelen parametreleri loglayarak doğrula
        _logger.LogInformation($"ID: {id}, CreatedBy: {createdBy}, CreatedById: {createdById}");

        if (string.IsNullOrEmpty(createdBy) || createdById <= 0)
        {
            return Json(new { success = false, message = "Geçersiz veri" });
        }

        // Müşteriyi veritabanında bul
        var customer = _context.Customers.Find(id);
        if (customer == null)
        {
            return Json(new { success = false, message = "Müşteri bulunamadı" });
        }

        // Müşteri zaten sahiplendiyse, kim tarafından sahiplenildiğini göster
        if (customer.IsOwned)
        {
            var owner = customer.CreatedBy ?? "Bilinmeyen";
            return Json(
                new
                {
                    success = false,
                    message = $"Bu müşteri zaten {owner} tarafından sahiplenilmiş.",
                }
            );
        }

        // Sahiplenme işlemini gerçekleştir
        var oldCreatedBy = customer.CreatedBy;
        var oldCreatedById = customer.CreatedById;

        customer.CreatedBy = createdBy;
        customer.CreatedById = createdById;
        customer.IsOwned = true;

        try
        {
            // Değişiklikleri logla
            AddLogIfChanged("CreatedBy", oldCreatedBy, createdBy, customer.Id);

            // Değişiklikleri kaydet
            _context.SaveChanges();

            // E-posta gönder
            SendNotificationDenetlemeEmail(customer);

            return Json(new { success = true, message = "Müşteri başarıyla sahiplenildi." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AssignCustomer işleminde bir hata oluştu.");
            return Json(new { success = false, message = $"Bir hata oluştu: {ex.Message}" });
        }
    }

    // E-posta gönderme fonksiyonu

    private void SendNotificationDenetlemeEmail(Customer customer)
    {
        string smtpServer = "smtp.turkticaret.net";
        int smtpPort = 587;
        string smtpUser = "byb@mutlucanozel.online";
        string smtpPassword = "Bybmutlu123.";

        string toEmail = "mutlu@bybetiket.com";
        string subject = "Yeni Müşteri Sahiplenildi";
        string body =
            $@"
    <!DOCTYPE html>
    <html lang='tr'>
    <head>
        <meta charset='UTF-8'>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <title>Müşteri Sahiplenildi</title>
        <style>
            body {{ font-family: Arial, sans-serif; }}
            .container {{ max-width: 600px; margin: auto; padding: 20px; background: #f4f4f4; border: 1px solid #ddd; }}
            .header {{ background: #1F253A; color: #fff; padding: 10px; text-align: center; font-size: 18px; }}
            .content {{ margin-top: 20px; font-size: 16px; }}
            .footer {{ margin-top: 20px; text-align: center; font-size: 14px; color: #666; }}
        </style>
    </head>
    <body>
        <div class='container'>
            <div class='header'>Yeni Müşteri Sahiplenildi</div>
            <div class='content'>
                <p><strong>Müşteri Adı:</strong> {customer.Name}</p>
                <p><strong>Sahiplenen Kişi:</strong> {customer.CreatedBy}</p>
                <p><strong>Sahiplenme Tarihi:</strong> {DateTime.Now:dd.MM.yyyy}</p>
                   <a href='{Url.Action("PotentialCustomerDetail", "Customer", new { id = customer.Id, }, Request.Scheme)}' class='button'>Detayları İncele</a>
            </div>
            <div class='footer'>© 2024 BYB CRM - Tüm Hakları Saklıdır.</div>
        </div>
    </body>
    </html>";

        try
        {
            using (var smtpClient = new SmtpClient(smtpServer))
            {
                smtpClient.Port = smtpPort;
                smtpClient.Credentials = new NetworkCredential(smtpUser, smtpPassword);
                smtpClient.EnableSsl = true;

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(smtpUser, "BYB|CRM"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true,
                };

                mailMessage.To.Add(toEmail);

                smtpClient.Send(mailMessage);
            }

            _logger.LogInformation("E-posta başarıyla gönderildi: {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-posta gönderilirken bir hata oluştu. Alıcı: {Email}", toEmail);
        }
    }

    public IActionResult Definition()
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
            DeliveryMethods = _context.DeliveryMethods.ToList(),
            OrderMethods = _context.OrderMethods.ToList(),
            MailInfos = _context.MailInfos.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(viewModel);
    }

    public IActionResult Costs(string name, double cost)
    {
        var model = new GeneralCostsViewModel
        {
            Machines = _context.Machines.ToList(),
            CombinationPaperAdhesives = _context.CombinationPaperAdhesives.ToList(),
            AdhesiveInfos = _context.AdhesiveInfos.ToList(),
            PaperInfos = _context.PaperInfos.ToList(),
            DeliveryMethods = _context.DeliveryMethods.ToList(),
            OrderMethods = _context.OrderMethods.ToList(),
            MailInfos = _context.MailInfos.ToList(),
            OtherCosts = _context.OtherCosts.ToList(),
            Terms = _context.Terms.ToList(),
            AdditionalProcessings = _context.AdditionalProcessings.ToList(),
        };

        return View(model);
    }

    [HttpGet]
    public JsonResult GetPriceHistoryCombination(int id)
    {
        var priceHistoryCombination = _context
            .CombinationPriceHistories.Where(h => h.CombinationId == id)
            .OrderByDescending(h => h.UpdatedAt)
            .Select(h => new { oldCost = h.OldCost, updatedAt = h.UpdatedAt })
            .ToList();

        return Json(priceHistoryCombination);
    }

    [HttpGet]
    public JsonResult GetPriceHistoryAdditional(int id)
    {
        var priceHistoryAdditional = _context
            .AdditionalProcessingCostHistories.Where(h => h.AdditionalProcessingId == id)
            .OrderByDescending(h => h.UpdatedAt)
            .Select(h => new { oldCost = h.OldCost, updatedAt = h.UpdatedAt })
            .ToList();

        return Json(priceHistoryAdditional);
    }

    [HttpGet]
    public JsonResult GetPriceHistoryOtherCost(int id)
    {
        var priceHistoryOtherCost = _context
            .OtherCostsHistories.Where(h => h.OtherCostId == id)
            .OrderByDescending(h => h.UpdatedAt)
            .Select(h => new { oldCost = h.OldCost, updatedAt = h.UpdatedAt })
            .ToList();

        return Json(priceHistoryOtherCost);
    }

    [HttpGet]
    public JsonResult GetPriceHistoryMachine(int id)
    {
        var priceHistoryMachine = _context
            .MachinePriceHistories.Where(h => h.MachineId == id)
            .OrderByDescending(h => h.UpdatedAt)
            .Select(h => new { oldCost = h.OldCost, updatedAt = h.UpdatedAt })
            .ToList();

        return Json(priceHistoryMachine);
    }

    [HttpGet]
    public JsonResult GetPriceHistoryTerm(int id)
    {
        var priceHistoryTerm = _context
            .TermPriceHistories.Where(h => h.TermId == id)
            .OrderByDescending(h => h.UpdatedAt)
            .Select(h => new { oldCost = h.OldCost, updatedAt = h.UpdatedAt })
            .ToList();

        return Json(priceHistoryTerm);
    }

    [HttpPost]
    public JsonResult SaveNotification([FromBody] AdminNotification notification)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(notification.Notification))
            {
                return Json(new { success = false, message = "Not alanı boş olamaz!" });
            }

            var existingNotification = _context.AdminNotification.FirstOrDefault();
            if (existingNotification != null)
            {
                existingNotification.Notification = notification.Notification;
                _context.AdminNotification.Update(existingNotification);
                _context.SaveChanges();
                return Json(new { success = true, message = "Not başarıyla güncellendi!" });
            }
            else
            {
                return Json(new { success = false, message = "Güncellenecek not bulunamadı!" });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Bir hata oluştu: " + ex.Message });
        }
    }

    [HttpPost]
    public IActionResult DeleteNotification()
    {
        var notification = _context.AdminNotification.FirstOrDefault();
        if (notification != null)
        {
            _context.AdminNotification.Remove(notification);
            _context.SaveChanges();
        }

        return RedirectToAction("Definition");
    }

    [HttpGet]
    [DynamicAuthorize("ListOffer")]
    public async Task<IActionResult> ListOffer(int page = 1, int pageSize = 1000)
    {
        IQueryable<Offer> query;

        if (
            User.IsInRole("Yönetici")
            || User.IsInRole("GENEL MÜDÜR")
            || User.IsInRole("Denetlemeci")
        )
        {
            query = _context
                .Offers.Include(o => o.Customer)
                .Include(o => o.PaperInfo)
                .Include(o => o.AdhesiveInfo)
                .Include(o => o.OrderMethod)
                .Include(o => o.DeliveryMethod);
        }
        else
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
                return View(
                    "Error",
                    new ErrorViewModel { RequestId = "User ID conversion failed." }
                );

            query = _context
                .Offers.Where(o => o.Customer.CreatedById == userId)
                .Include(o => o.Customer)
                .Include(o => o.PaperInfo)
                .Include(o => o.AdhesiveInfo)
                .Include(o => o.DeliveryMethod)
                .Include(o => o.OrderMethod);
        }

        int totalOffers = await query.CountAsync();

        var offers = await query
            .OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.TotalPages = (int)Math.Ceiling((double)totalOffers / pageSize);
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;

        return View(offers);
    }

    [HttpGet]
    public async Task<IActionResult> RecordList()
    {
        if (
            User.IsInRole("Yönetici")
            || User.IsInRole("GENEL MÜDÜR")
            || User.IsInRole("Denetlemeci")
        )
        {
            // Yönetici rolündeki kullanıcılar tüm kayıtları görür
            var records = await _context
                .Records.Include(r => r.Customer) // Müşteri bilgisi dahil ediliyor
                .OrderByDescending(r => r.Id) // ID'ye göre büyükten küçüğe sıralama
                .ToListAsync();

            return View(records); // View'e Record modelini gönder
        }
        else
        {
            // Yönetici değilse, sadece kendi kayıtlarını görür
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return View(
                    "Error",
                    new ErrorViewModel { RequestId = "User ID conversion failed." }
                );
            }

            var records = await _context
                .Records.Where(r => r.Customer.CreatedById == userId) // Kullanıcının oluşturduğu kayıtlar
                .Include(r => r.Customer) // Müşteri bilgisi dahil ediliyor
                .OrderByDescending(r => r.Id) // ID'ye göre büyükten küçüğe sıralama
                .ToListAsync();

            return View(records); // View'e Record modelini gönder
        }
    }
}

public class City
{
    public string Name { get; set; }
    public List<string> Districts { get; set; }
}
