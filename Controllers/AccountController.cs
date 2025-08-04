using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using crm.Data;
using crm.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OfficeOpenXml;

namespace crm.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly HttpClient _httpClient;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly string _profilePicturesPath;

        public AccountController(
            ApplicationDbContext context,
            ILogger<AccountController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IWebHostEnvironment env
        )
        {
            _env = env;
            _context = context;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;

            _profilePicturesPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "profile_pictures"
            );

            if (!Directory.Exists(_profilePicturesPath))
            {
                Directory.CreateDirectory(_profilePicturesPath);
            }
        }

        [HttpPost]
        public IActionResult TransferInfo([FromBody] TransferInfoRequest request)
        {
            if (
                request.SourceCustomerId <= 0
                || request.TargetCustomerId <= 0
                || request.InfoTypes == null
            )
            {
                return BadRequest(
                    new { success = false, message = "Tüm bilgileri doldurmanız gerekiyor." }
                );
            }

            List<string> transferredInfo = new List<string>(); // Transfer edilen bilgileri tutar

            // İrtibat bilgilerini aktar
            if (request.InfoTypes.Contains("contacts"))
            {
                var contacts = _context
                    .Contacts.Where(c => c.CustomerId == request.SourceCustomerId)
                    .ToList();
                contacts.ForEach(c => c.CustomerId = request.TargetCustomerId);
                _context.SaveChanges();
                transferredInfo.Add("İrtibat Bilgileri");
            }

            // Lokasyon bilgilerini aktar
            if (request.InfoTypes.Contains("locations"))
            {
                var locations = _context
                    .Locations.Where(l => l.CustomerId == request.SourceCustomerId)
                    .ToList();
                locations.ForEach(l => l.CustomerId = request.TargetCustomerId);
                _context.SaveChanges();
                transferredInfo.Add("Lokasyon Bilgileri");
            }

            // Kayıt bilgilerini aktar
            if (request.InfoTypes.Contains("records"))
            {
                var records = _context
                    .Records.Where(r => r.CustomerId == request.SourceCustomerId)
                    .ToList();
                records.ForEach(r => r.CustomerId = request.TargetCustomerId);
                _context.SaveChanges();
                transferredInfo.Add("Kayıt Bilgileri");
            }

            // Teklif bilgilerini aktar
            if (request.InfoTypes.Contains("offers"))
            {
                var offers = _context
                    .Offers.Where(o => o.CustomerId == request.SourceCustomerId)
                    .ToList();
                offers.ForEach(o => o.CustomerId = request.TargetCustomerId);
                _context.SaveChanges();
                transferredInfo.Add("Teklif Bilgileri");
            }

            // Log bilgilerini aktar
            if (request.InfoTypes.Contains("logs"))
            {
                var logs = _context
                    .ChangeLogs.Where(o => o.RecordId == request.SourceCustomerId)
                    .ToList();
                logs.ForEach(o => o.RecordId = request.TargetCustomerId);
                _context.SaveChanges();
                transferredInfo.Add("Log Bilgileri");
            }

            // Log kaydı oluştur
            if (transferredInfo.Any())
            {
                string infoDetails = string.Join(", ", transferredInfo);
                LogChange(
                    "Customers",
                    request.TargetCustomerId,
                    $"Transfer edilen bilgiler: {infoDetails}",
                    $"Kaynak Müşteri Id: {request.SourceCustomerId}",
                    $"Hedef Müşteri Id: {request.TargetCustomerId}",
                    $"Transfer Edildi"
                );
            }

            // Eğer kaynak müşteri silinmek isteniyorsa
            if (request.DeleteSourceCustomer)
            {
                var sourceCustomer = _context.Customers.Find(request.SourceCustomerId);
                if (sourceCustomer != null)
                {
                    _context.Customers.Remove(sourceCustomer);
                    _context.SaveChanges();
                    return Ok(
                        new
                        {
                            success = true,
                            message = "Bilgiler başarıyla aktarıldı ve kaynak müşteri silindi.",
                        }
                    );
                }
                else
                {
                    return Ok(
                        new
                        {
                            success = true,
                            message = "Bilgiler aktarıldı ancak kaynak müşteri bulunamadı.",
                        }
                    );
                }
            }

            return Ok(new { success = true, message = "Bilgiler başarıyla aktarıldı." });
        }

        public IActionResult TransferInfo()
        {
            var customers = _context
                .Customers.Select(c => new Customer { Id = c.Id, Name = c.Name })
                .ToList();

            var viewModel = new TransferViewModel
            {
                Customers = customers,
                TransferInfoRequest = new TransferInfoRequest(),
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file != null && file.Length > 0)
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }

                var filePath = Path.Combine(uploadsDir, file.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                return Ok(new { success = true, filePath = $"/uploads/{file.FileName}" });
            }

            return BadRequest(new { success = false, message = "Dosya yüklenemedi." });
        }

        // Tüm dosyaları listeleme işlemi
        [HttpGet]
        public IActionResult GetFiles()
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsDir))
            {
                return Ok(new string[] { });
            }

            var files = Directory.GetFiles(uploadsDir).Select(Path.GetFileName).ToList();
            return Ok(files);
        }

        [HttpGet("get-last-modified")]
        public IActionResult GetLastModified()
        {
            var filePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot/profile_pictures/tahsilat.xlsx"
            );

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { message = "Dosya bulunamadı." });
            }

            var lastModified = System.IO.File.GetLastWriteTimeUtc(filePath);
            return Ok(new { lastModified = lastModified.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        [DynamicAuthorize("AccountingReport")]
        [HttpGet]
        public IActionResult AccountingReport()
        {
            // Boş bir tablo için başlangıçta hiçbir veri döndürmüyoruz
            return View();
        }

        [HttpGet("/veriler.json")]
        public IActionResult GetJsonFromExcel()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            string excelPath = Path.Combine(_env.WebRootPath, "profile_pictures", "tahsilat.xlsx");

            using (var stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read))
            {
                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets[0];

                var result = new List<List<object>>();
                int rowCount = worksheet.Dimension.Rows;
                int colCount = worksheet.Dimension.Columns;

                for (int row = 1; row <= rowCount; row++)
                {
                    var rowData = new List<object>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        rowData.Add(worksheet.Cells[row, col].Text);
                    }
                    result.Add(rowData);
                }

                return Json(result);
            }
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [DynamicAuthorize("ListUser")]
        [HttpGet]
        public IActionResult ListUser()
        {
            try
            {
                var users = _context
                    .Users.Include(u => u.UserRoles)
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        FirstName = u.FirstName,
                        LastName = u.LastName,
                        Email = u.Email,
                        PhoneNumber = u.PhoneNumber,
                        Roles = u.UserRoles.Select(ur => ur.Role),
                        ProfilePicturePath = u.ProfilePicturePath,
                    })
                    .ToList();

                var roles = _context.Roles.ToList();
                ViewBag.Roles = roles;

                _logger.LogInformation($"Retrieved {users.Count} users from the database.");

                return View(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users list.");
                TempData["ErrorMessage"] = "Error retrieving users list.";
                return RedirectToAction("Error");
            }
        }

        [DynamicAuthorize("EditUser")]
        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            var user = await _context
                .Users.Include(u => u.UserRoles)
                .SingleOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Error");
            }
            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = roles;
            return View(user);
        }

        [DynamicAuthorize("DeleteUser")]
        [HttpPost]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return Json(new { success = false });
            }

            // Assume that the CreatedBy field contains the user's full name (FirstName + " " + LastName)
            string createdByValue = $"{user.FirstName} {user.LastName}";

            // Fetch customers created by this user
            var customersCreatedByUser = await _context
                .Customers.Where(c => c.CreatedBy == createdByValue)
                .ToListAsync();

            if (customersCreatedByUser.Any())
            {
                string errorMessage = "Kullanıcının sorumlu olduğu müşteriler var, silinemez!";
                TempData["ErrorMessage"] = errorMessage;
                return Json(new { success = false, message = errorMessage });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> NewUser()
        {
            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = roles;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(User model)
        {
            // Remove unnecessary model validation fields
            ModelState.Remove("profilePicture");
            ModelState.Remove("FirstName");
            ModelState.Remove("LastName");
            ModelState.Remove("PhoneNumber");

            try
            {
                if (!ModelState.IsValid)
                {
                    TempData["ErrorMessage"] = "Lütfen tüm alanları doğru şekilde doldurun.";
                    return View("Login", model);
                }

                // Check for empty email or password
                if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
                {
                    TempData["ErrorMessage"] = "E-posta ve şifre alanları boş bırakılamaz.";
                    return View("Login", model);
                }

                // Check if location is required (non-local, HTTPS requests)
                bool isLocal =
                    HttpContext.Request.Host.Host == "localhost"
                    || HttpContext.Request.Host.Host == "127.0.0.1";
                bool isHttps = HttpContext.Request.IsHttps;

                if (!isLocal && isHttps && string.IsNullOrEmpty(model.Location))
                {
                    ModelState.AddModelError("Location", "Konum bilgisi gereklidir.");
                    return View("Login", model);
                }

                // Authenticate user credentials
                if (!AuthenticateUser(model.Email, model.Password))
                {
                    TempData["ErrorMessage"] = "Geçersiz e-posta veya şifre.";
                    return View("Login", model);
                }

                // Fetch the user from the database
                var user = await _context
                    .Users.Include(u => u.UserRoles)
                    .SingleOrDefaultAsync(u => u.Email == model.Email);

                if (user == null)
                {
                    TempData["ErrorMessage"] = "Kullanıcı bulunamadı.";
                    return View("Login", model);
                }

                // Create user claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, model.Email),
                    new Claim("FirstName", user.FirstName),
                    new Claim("LastName", user.LastName),
                    new Claim("FullName", $"{user.FirstName} {user.LastName}"),
                    new Claim(
                        "ProfilePicturePath",
                        user.ProfilePicturePath ?? "/profile_pictures/default_byb.png"
                    ),
                };

                // Add user roles to claims
                claims.AddRange(
                    user.UserRoles.Select(role => new Claim(ClaimTypes.Role, role.Role.ToString()))
                );

                // Create claims identity and sign in
                var claimsIdentity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme
                );
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity)
                );

                // Record login history
                _context.LoginHistories.Add(
                    new LoginHistory
                    {
                        UserId = user.Id,
                        LoginTime = DateTime.Now,
                        Location = model.Location,
                    }
                );
                await _context.SaveChangesAsync();

                // Set culture to Turkish
                var cultureInfo = new CultureInfo("tr-TR");
                CultureInfo.CurrentCulture = cultureInfo;
                CultureInfo.CurrentUICulture = cultureInfo;

                // Set success messages
                TempData["LoginSuccess"] = true;
                TempData["UserId"] = user.Id;
                TempData["UserName"] = $"{user.FirstName} {user.LastName}";

                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during login.");
                TempData["ErrorMessage"] = "Bir hata meydana geldi, lütfen tekrar deneyin.";
            }

            return View("Login", model);
        }

        [HttpGet]
        [DynamicAuthorize("TransferCustomers")]
        public IActionResult TransferCustomers()
        {
            var users = _context
                .Users.Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    CustomerCount = _context.Customers.Count(c => c.CreatedById == u.Id), // Count using CreatedById
                })
                .ToList();

            var model = new TransferViewModel
            {
                Users = users
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        FirstName = u.FirstName,
                        LastName = u.LastName,
                        // UserViewModel does not need CustomerCount
                    })
                    .ToList(),
            };

            ViewBag.UserCounts = users.ToDictionary(u => u.Id, u => u.CustomerCount);

            return View(model);
        }

        [DynamicAuthorize("TransferCustomers")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult TransferCustomers(int sourceUserId, int targetUserId)
        {
            try
            {
                if (sourceUserId == targetUserId)
                {
                    return Json(
                        new { success = false, message = "Kaynak ve hedef kullanıcı aynı olamaz." }
                    );
                }

                // Kaynak kullanıcıya ait müşterileri kontrol et
                var customers = _context
                    .Customers.Where(c => c.CreatedById == sourceUserId)
                    .ToList();

                if (customers.Count == 0)
                {
                    return Json(
                        new
                        {
                            success = false,
                            message = "Kaynak müşteri sorumlusunun sahip olduğu müşteri yoktur.",
                        }
                    );
                }

                // Hedef kullanıcı bilgilerini getir
                var targetUser = _context.Users.FirstOrDefault(u => u.Id == targetUserId);
                if (targetUser == null)
                {
                    _logger.LogError($"Hedef kullanıcı (ID:{targetUserId}) bulunamadı.");
                    return Json(new { success = false, message = "Hedef kullanıcı bulunamadı." });
                }

                // Müşterileri hedef kullanıcıya aktar
                foreach (var customer in customers)
                {
                    var oldCreatedBy = customer.CreatedBy ?? "N/A";
                    var oldCreatedById = customer.CreatedById;

                    customer.CreatedById = targetUserId;
                    customer.CreatedBy = $"{targetUser.FirstName} {targetUser.LastName}";

                    // Her müşteri aktarımı için log kaydı
                    LogChange(
                        "Customers",
                        customer.Id,
                        $"Müşteri Transferi ",
                        $"{oldCreatedBy} (ID:{oldCreatedById})",
                        $"{customer.CreatedBy} (ID:{targetUserId})",
                        "Transfer Edildi"
                    );
                }

                _context.SaveChanges();

                return Json(new { success = true, message = "Müşteriler başarıyla aktarıldı." });
            }
            catch (Exception ex)
            {
                // Hata durumunda logla
                _logger.LogError(ex, "Müşteri aktarımı sırasında bir hata oluştu.");
                return Json(
                    new { success = false, message = "Bir hata oluştu, lütfen tekrar deneyin." }
                );
            }
        }

        private void LogChange(
            string tableName,
            int recordId,
            string columnName,
            string oldValue,
            string newValue,
            string operationType
        )
        {
            // Kullanıcı ad ve soyadını Claims'den alıyoruz
            var firstName =
                User?.Claims.FirstOrDefault(c => c.Type == "FirstName")?.Value ?? "Bilinmeyen";
            var lastName =
                User?.Claims.FirstOrDefault(c => c.Type == "LastName")?.Value ?? "Kullanıcı";

            var fullName = $"{firstName} {lastName}"; // Ad ve soyad birleştiriliyor

            var log = new ChangeLog
            {
                TableName = tableName,
                RecordId = recordId,
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

        [DynamicAuthorize("LogRecords")]
        public IActionResult LogRecords(int page = 1, int pageSize = 3000)
        {
            var skip = (page - 1) * pageSize;

            var query =
                from log in _context.ChangeLogs
                join customer in _context.Customers
                    on log.RecordId equals customer.Id
                    into customerGroup
                from customer in customerGroup.DefaultIfEmpty()
                orderby log.ChangedAt descending
                select new
                {
                    log.TableName,
                    log.RecordId,
                    log.ColumnName,
                    log.OldValue,
                    log.NewValue,
                    log.OperationType,
                    log.ChangedBy,
                    log.ChangedAt,
                    CustomerName = customer != null ? customer.Name : "Müşteri Bulunamadı",
                };

            var totalCount = query.Count();

            var logs = query.Skip(skip).Take(pageSize).ToList();

            ViewBag.TotalCount = totalCount;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            return View(logs);
        }

        [HttpGet]
        [DynamicAuthorize("ListLoginHistory")]
        public IActionResult ListLoginHistory(
            DateTime? startDate,
            DateTime? endDate,
            int page = 1,
            int pageSize = 3000
        )
        {
            var query = _context
                .LoginHistories.Include(l => l.User)
                .ThenInclude(u => u.UserRoles)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(l => l.LoginTime >= startDate.Value.Date);

            if (endDate.HasValue)
                query = query.Where(l => l.LoginTime <= endDate.Value.Date.AddDays(1).AddTicks(-1));

            var totalCount = query.Count();

            var loginHistories = query
                .OrderByDescending(l => l.LoginTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new
                {
                    l.User.FirstName,
                    l.User.LastName,
                    l.User.Email,
                    Roles = string.Join(",", l.User.UserRoles.Select(r => r.Role)),
                    l.LoginTime,
                    l.Location,
                })
                .ToList();

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");

            return View(loginHistories);
        }

        private bool AuthenticateUser(string email, string password)
        {
            var user = _context.Users.SingleOrDefault(u => u.Email == email);
            if (user != null)
            {
                if (VerifyPassword(password, user.Password))
                {
                    return true;
                }
            }
            return false;
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
            }
        }

        private bool VerifyPassword(string inputPassword, string hashedPassword)
        {
            var inputHashed = HashPassword(inputPassword);
            return inputHashed == hashedPassword;
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [DynamicAuthorize("EditUser")]
        [HttpPost]
        public async Task<IActionResult> EditUser(
            User model,
            IFormFile profilePicture,
            string[] selectedRoles
        )
        {
            ModelState.Remove("profilePicture");
            ModelState.Remove("password"); // Şifre zorunlu olmadığında hatayı engelliyoruz
            if (ModelState.IsValid)
            {
                var user = await _context
                    .Users.Include(u => u.UserRoles)
                    .SingleOrDefaultAsync(u => u.Id == model.Id);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "User not found.";
                    return Json(new { success = false });
                }

                if (await _context.Users.AnyAsync(u => u.Email == model.Email && u.Id != model.Id))
                {
                    TempData["ErrorMessage"] = "Email address already in use.";
                    return Json(new { success = false });
                }

                // Profil resmini güncelleme işlemi
                if (profilePicture != null && profilePicture.Length > 0)
                {
                    var fileName =
                        Guid.NewGuid().ToString() + Path.GetExtension(profilePicture.FileName);
                    var filePath = Path.Combine(_profilePicturesPath, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await profilePicture.CopyToAsync(stream);
                    }
                    model.ProfilePicturePath = "/profile_pictures/" + fileName;

                    if (!string.IsNullOrEmpty(user.ProfilePicturePath))
                    {
                        var oldFilePath = Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot",
                            user.ProfilePicturePath.TrimStart('/')
                        );
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            System.IO.File.Delete(oldFilePath);
                        }
                    }

                    user.ProfilePicturePath = model.ProfilePicturePath;
                }
                else
                {
                    model.ProfilePicturePath = user.ProfilePicturePath;
                }

                // Kullanıcı bilgilerini güncelleme
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;

                // Eğer şifre alanı doluysa, şifreyi güncelle
                if (!string.IsNullOrEmpty(model.Password))
                {
                    user.Password = HashPassword(model.Password); // HashPassword metodunu kullanarak şifreyi güncelle
                }

                // Rolleri güncelleme
                user.UserRoles.Clear();
                foreach (var role in selectedRoles)
                {
                    user.UserRoles.Add(new UserRoleEntity { UserId = user.Id, Role = role });
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }

            var errors = ModelState
                .Values.SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            TempData["ErrorMessage"] =
                "Başarısız, lütfen tüm alanları doldurunuz. " + string.Join(", ", errors) + ".";
            return Json(new { success = false, message = TempData["ErrorMessage"] });
        }

        [HttpPost]
        public async Task<IActionResult> NewUser(
            User model,
            IFormFile profilePicture,
            string[] selectedRoles
        )
        {
            try
            {
                ModelState.Remove("profilePicture"); // profilePicture alanını model doğrulamasından çıkar

                if (selectedRoles == null || selectedRoles.Length == 0)
                {
                    return Json(
                        new { success = false, message = "En az bir rol seçmelisiniz 👨🏼‍🔧" }
                    );
                }

                if (ModelState.IsValid)
                {
                    if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                    {
                        return Json(
                            new
                            {
                                success = false,
                                message = "Bu e-posta adresine sahip bir kullanıcı zaten mevcut 👨🏼‍🔧 ",
                            }
                        );
                    }

                    if (profilePicture != null && profilePicture.Length > 0)
                    {
                        var fileName =
                            Guid.NewGuid().ToString() + Path.GetExtension(profilePicture.FileName);
                        var filePath = Path.Combine(_profilePicturesPath, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await profilePicture.CopyToAsync(stream);
                        }
                        model.ProfilePicturePath = "/profile_pictures/" + fileName;
                    }
                    else
                    {
                        model.ProfilePicturePath = "/profile_pictures/default.png"; // Varsayılan profil fotoğrafı
                    }

                    model.Password = HashPassword(model.Password);

                    foreach (var role in selectedRoles)
                    {
                        model.UserRoles.Add(new UserRoleEntity { Role = role });
                    }

                    _context.Users.Add(model);
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = "Kayıt başarılı  🥳" });
                }
                else
                {
                    var errors = ModelState
                        .Values.SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList();
                    return Json(
                        new
                        {
                            success = false,
                            message = "Lütfen tüm alanları doldurun: "
                                + string.Join(", ", errors)
                                + ".",
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Yeni kullanıcı kaydı sırasında bir hata oluştu ⛔️");
                return Json(
                    new
                    {
                        success = false,
                        message = "Tüm alanları doldurunuz. Hata : " + ex.Message,
                    }
                );
            }
        }

        [Authorize(Roles = "Yönetici")]
        [HttpGet]
        public async Task<IActionResult> ManageRoles()
        {
            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = roles;

            var pageAccesses = await _context
                .PageAccesses.Include(p => p.RolePageAccesses)
                .ThenInclude(rpa => rpa.Role)
                .ToListAsync();

            var model = pageAccesses
                .Select(pa => new PageAccessViewModel
                {
                    PageName = pa.PageName,
                    DisplayName = pa.DisplayName,
                    Roles = pa.RolePageAccesses.Select(rpa => rpa.Role.Name).ToList(),
                })
                .ToList();

            return View(model);
        }

        [Authorize(Roles = "Yönetici")]
        [HttpPost]
        public async Task<IActionResult> ManageRoles(List<string> roleAccess)
        {
            try
            {
                var roles = await _context.Roles.ToListAsync();
                var pageAccesses = await _context
                    .PageAccesses.Include(p => p.RolePageAccesses)
                    .ThenInclude(rpa => rpa.Role)
                    .ToListAsync();

                bool isUpdated = false;

                // Mevcut rol ve erişim bilgilerini al
                var currentRoleAccessList = pageAccesses
                    .SelectMany(pa =>
                        pa.RolePageAccesses.Select(rpa => $"{pa.PageName}-{rpa.Role.Name}")
                    )
                    .ToList();

                // Yeni roleAccess ile mevcut olanları karşılaştır
                if (
                    !roleAccess.Except(currentRoleAccessList).Any()
                    && !currentRoleAccessList.Except(roleAccess).Any()
                )
                {
                    // Eğer gelen roleAccess ve mevcut roller aynı ise değişiklik yapılmadı.
                    return Json(
                        new { success = false, message = "Herhangi bir değişiklik yapılmadı." }
                    );
                }

                // Eğer farklılık varsa güncelleme yap
                foreach (var pageAccess in pageAccesses)
                {
                    pageAccess.RolePageAccesses.Clear();
                }

                foreach (var access in roleAccess)
                {
                    var parts = access.Split('-');
                    var pageName = parts[0];
                    var roleName = parts[1];

                    var pageAccess = pageAccesses.FirstOrDefault(pa => pa.PageName == pageName);
                    var role = roles.FirstOrDefault(r => r.Name == roleName);

                    if (pageAccess != null && role != null)
                    {
                        pageAccess.RolePageAccesses.Add(
                            new RolePageAccess { RoleId = role.Id, PageAccessId = pageAccess.Id }
                        );
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while managing roles.");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddRole(string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                TempData["ErrorMessage"] = "Rol boş olamaz.";
                return Json(new { success = false, message = TempData["ErrorMessage"].ToString() });
            }

            var existingRole = await _context.Roles.FirstOrDefaultAsync(r =>
                r.Name.ToUpper() == roleName.ToUpper()
            );
            if (existingRole != null)
            {
                TempData["ErrorMessage"] = "Bu rol zaten mevcut.";
                return Json(new { success = false, message = TempData["ErrorMessage"].ToString() });
            }

            var role = new Role { Name = roleName };
            _context.Roles.Add(role);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Rol başarıyla eklendi 🥳";
            return Json(
                new
                {
                    success = true,
                    role,
                    message = TempData["SuccessMessage"].ToString(),
                }
            );
        }

        [HttpPost]
        public async Task<IActionResult> DeleteRole(int roleId)
        {
            var role = await _context.Roles.FindAsync(roleId);
            if (role == null)
            {
                TempData["ErrorMessage"] = "Role not found.";
                return Json(new { success = false, message = "Rol bulunamadı." });
            }

            var usersWithRole = await _context.Users.AnyAsync(u =>
                u.UserRoles.Any(ur => ur.Role.ToLower() == role.Name.ToLower())
            );
            if (usersWithRole)
            {
                TempData["ErrorMessage"] =
                    "Bu role sahip kullanıcı mevcut.Silmek istediğiniz role ait kullanıcı olmadığından emin olun.";
                return Json(
                    new
                    {
                        success = false,
                        message = "Bu role sahip kullanıcı mevcut. Silmek istediğiniz role ait kullanıcı olmadığından emin olun.",
                    }
                );
            }

            _context.Roles.Remove(role);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Rol başarıyla silindi 🥳";
            return Json(new { success = true, message = "Rol başarıyla silindi 🥳" });
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
