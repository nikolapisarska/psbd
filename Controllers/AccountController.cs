using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using BCrypt.Net;

namespace meow.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly ILogger<AccountController> _logger; // Systemowy Logger zdarzeń
        private readonly IEmailService _emailService; // Nowo dodana usługa mailingu

        // Konstruktor realizujący wstrzykiwanie zależności (Dependency Injection) z uwzględnieniem IEmailService
        public AccountController(LibraryDbContext context, ILogger<AccountController> logger, IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
        }

        // ==========================================================
        // 1. PROFIL UŻYTKOWNIKA (GET) - STRONA "MOJE KONTO"
        // ==========================================================
        [HttpGet]
        public IActionResult Profile()
        {
            var userLogin = HttpContext.Session.GetString("User");
            var userRole = HttpContext.Session.GetString("UserRole");

            // Zabezpieczenie przekierowania dla administratora
            if (userRole == "Admin")
            {
                return RedirectToAction("Index", "Admin"); 
            }

            if (string.IsNullOrEmpty(userLogin))
            {
                return RedirectToAction("Login", "Account");
            }

            var userInDb = _context.Users.Include(u => u.Klient).FirstOrDefault(u => u.Login == userLogin);
            if (userInDb == null || userInDb.Klient == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var klient = userInDb.Klient;

            var nienaliczoneKary = _context.Platnosci
                .Include(p => p.Wypozyczenie).ThenInclude(w => w!.Egzemplarz).ThenInclude(e => e!.Book)
                .Where(p => p.Wypozyczenie != null && p.Wypozyczenie.IdKlient == klient.IdKlienta)
                .ToList();

            var finesList = nienaliczoneKary.Select(p => new FineItemViewModel
            {
                Id = p.IdPlatnosc,
                BookTitle = p.Wypozyczenie?.Egzemplarz?.Book?.Tytul ?? "Opłata regulaminowa",
                Amount = p.Kwota,
                DateGenerated = p.Wypozyczenie?.DataZwrotu?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd")
            }).ToList();

            var suroweWypozyczenia = _context.Wypozyczenia
                .Include(w => w.Egzemplarz).ThenInclude(e => e!.Book)
                .Where(w => w.IdKlient == klient.IdKlienta && w.IdEgzemplarz != null)
                .ToList(); 

            var zmapowaneWypozyczenia = suroweWypozyczenia.Select(w => new RentalHistoryItem
            {
                Id = w.IdWypozyczenie,
                BookTitle = w.Egzemplarz != null && w.Egzemplarz.Book != null ? w.Egzemplarz.Book.Tytul : "Nieznany tytuł",
                RentalDate = w.DataWypozyczenia.ToString("yyyy-MM-dd"),
                Status = w.DataZwrotu.HasValue ? "Rozliczone" : (DateTime.Today > w.DataPlanowanegoZwrotu ? "Zaległość" : "Aktywne"),
                ReturnDate = w.DataZwrotu.HasValue 
                    ? $"Zwrócono ({w.DataZwrotu.Value.ToString("yyyy-MM-dd")})" 
                    : (DateTime.Today > w.DataPlanowanegoZwrotu 
                        ? $"Po terminie o {(DateTime.Today - w.DataPlanowanegoZwrotu).Days} dni" 
                        : $"Zostało {(w.DataPlanowanegoZwrotu - DateTime.Today).Days} dni")
            }).ToList();

            var model = new ProfileViewModel
            {
                CustomerName = $"{klient.Imie} {klient.Nazwisko}",
                Rentals = zmapowaneWypozyczenia,

                Packages = _context.Zamowienia
                    .Include(z => z.Book)
                    .Where(z => z.IdKlienta == klient.IdKlienta)
                    .ToList()
                    .GroupBy(z => new { z.DataZamowienia, z.NumerSledzenia })
                    .Select(group => {
                        var pierwsze = group.First();
                        
                        var wewnętrznePozycje = group
                            .Where(z => z.Book != null)
                            .GroupBy(z => z.IdKsiazki)
                            .Select(bGroup => {
                                var ksiazka = bGroup.First().Book!;
                                return new OrderItemViewModel
                                {
                                    Title = ksiazka.Tytul,
                                    Author = ksiazka.Autor,
                                    Price = ksiazka.Cena ?? 0.00m,
                                    ImageUrl = ksiazka.ImageUrl ?? "/images/default-cover.jpg",
                                    Quantity = bGroup.Count()
                                };
                            }).ToList();

                        decimal łącznaSuma = wewnętrznePozycje.Sum(item => item.Price * item.Quantity);

                        return new OrderGroupViewModel
                        {
                            OrderId = pierwsze.Id,
                            OrderDate = pierwsze.DataZamowienia.ToString("yyyy-MM-dd HH:mm"),
                            Status = pierwsze.Status,
                            TrackingNumber = pierwsze.NumerSledzenia ?? "Brak numeru",
                            TotalPrice = łącznaSuma,
                            Items = wewnętrznePozycje
                        };
                    })
                    .OrderByDescending(o => o.OrderDate)
                    .ToList(),

                Fines = finesList,
                TotalFinesAmount = finesList.Sum(f => f.Amount)
            };

            return View(model);
        }

        // ==========================================================
        // 2. LOGOWANIE (GET)
        // ==========================================================
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // ==========================================================
        // 3. LOGOWANIE (POST) - POŁĄCZONA I ZABEZPIECZONA METODA ASYNCHRONICZNA
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public async Task<IActionResult> Login(string login, string haslo) // <-- Zmiana tutaj
        {
            // Walidacja pustych pól formularza
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(haslo)) // <-- Zmiana tutaj
            {
                TempData["Message"] = "Uzupełnij login i hasło.";
                TempData["MessageType"] = "error";
                return View();
            }

            // Asynchroniczne szukanie użytkownika w bazie danych wraz z profilem klienta
            var user = await _context.Users
                .Include(u => u.Klient)
                .FirstOrDefaultAsync(u => u.Login == login); 

            // Weryfikacja loginu oraz weryfikacja hasła kryptograficznego przy użyciu BCrypt
            if (user != null && BCrypt.Net.BCrypt.Verify(haslo, user.Haslo)) // <-- Zmiana tutaj
            {
                // --- PUNKT 19: LOGGER (SUKCES LOGOWANIA) ---
                _logger.LogInformation("Użytkownik '{Login}' pomyślnie zalogował się do systemu. Rola: {Rola}. Adres IP: {IP}", 
                    login, user.Rola ?? "Klient", HttpContext.Connection.RemoteIpAddress);

                // Zapisywanie danych uwierzytelniających w sesji serwera
                HttpContext.Session.SetString("User", user.Login ?? "Użytkownik");
                HttpContext.Session.SetString("Role", user.Rola ?? "Klient");
                HttpContext.Session.SetString("UserRole", user.Rola ?? "Klient"); // Dla kompatybilności z akcją Profile

                if (user.KlientId.HasValue)
                {
                    HttpContext.Session.SetInt32("UserId", user.KlientId.Value);
                }
                else
                {
                    var powiazanyKlient = await _context.Klienci.FirstOrDefaultAsync(k => k.Email == user.Login);
                    if (powiazanyKlient != null)
                    {
                        HttpContext.Session.SetInt32("UserId", powiazanyKlient.IdKlienta);
                    }
                }

                TempData["Message"] = $"Witaj ponownie, {user.Login}! 🐾";
                TempData["MessageType"] = "success";
                
                return RedirectToAction("Index", "Home");
            }
            else
            {
                // --- PUNKT 19: LOGGER (OSTRZEŻENIE O ZŁYM LEŚNYM LOGOWANIU) ---
                _logger.LogWarning("Nieudana próba logowania na konto '{Login}'. Podano błędne hasło lub użytkownik nie istnieje.", 
                    login);

                TempData["Message"] = "Nieprawidłowy login lub hasło.";
                TempData["MessageType"] = "error";
                ViewBag.Error = "Nieprawidłowy login lub hasło!";
                return View();
            }
        }

        // ==========================================================
        // 4. REJESTRACJA (GET)
        // ==========================================================
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // ==========================================================
        // 5. REJESTRACJA (POST) - ZMIENIONA NA ASYNCHRONICZNĄ Z USŁUGĄ MAILINGU
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public async Task<IActionResult> Register([Bind("Login,Haslo,Email,Imie,Nazwisko,Telefon")] RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = "Wypełnij poprawnie wszystkie wymagane pola!";
                return View(model);
            }
            if (_context.Users.Any(u => u.Login == model.Login))
            {
                ViewBag.Error = "Taki użytkownik już istnieje!";
                return View(model);
            }

            if (_context.Klienci.Any(k => k.Email == model.Email))
            {
                ViewBag.Error = "Ten adres e-mail jest już zajęty!";
                return View(model);
            }

            var newKlient = new Klient
            {
                Imie = model.Imie,
                Nazwisko = model.Nazwisko,
                Email = model.Email,
                Telefon = string.IsNullOrEmpty(model.Telefon) ? "-" : model.Telefon
            };

            _context.Klienci.Add(newKlient);
            _context.SaveChanges(); 

            var hashedHaslo = BCrypt.Net.BCrypt.HashPassword(model.Haslo);
            string przydzielonaRola = "Klient"; 

            var newUser = new User 
            { 
                Login = model.Login, 
                Haslo = hashedHaslo,
                Rola = przydzielonaRola,
                KlientId = newKlient.IdKlienta
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            // --- PUNKT 13: MAILING (WYSOŁANIE MAILA POWITALNEGO) ---
            // Wywołujemy asynchroniczną usługę wysyłania e-maila po poprawnym zarejestrowaniu użytkownika.
            await _emailService.SendWelcomeEmailAsync(model.Email, model.Login);

            return RedirectToAction("Login");
        }

        // ==========================================================
// 6. WYLOGOWANIE (POST)
// ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }
    }
}