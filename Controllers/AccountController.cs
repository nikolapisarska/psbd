using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BCrypt.Net;

namespace meow.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly LibraryDbContext _context;

        public AccountController(LibraryDbContext context)
        {
            _context = context;
        }

        // ==========================================================
        // 1. PROFIL UŻYTKOWNIKA (GET) - STRONA "MOJE KONTO"
        // ==========================================================
        [HttpGet]
        public IActionResult Profile()
        {
            // 1. Pobieramy login tekstowy z sesji
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

            // 2. Szukamy użytkownika w bazie na podstawie loginu i dołączamy profil Klienta
            var userInDb = _context.Users.Include(u => u.Klient).FirstOrDefault(u => u.Login == userLogin);
            if (userInDb == null || userInDb.Klient == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var klient = userInDb.Klient;

            // 3. Pobranie i zmapowanie kar finansowych
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
                .ToList(); // Najpierw pobieramy dane z MySQL, żeby uniknąć błędu rzutowania dat

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

            // 4. Budowanie pełnego modelu widoku profilu
            var model = new ProfileViewModel
            {
                CustomerName = $"{klient.Imie} {klient.Nazwisko}",
                Rentals = zmapowaneWypozyczenia, // Przekazujemy bezpiecznie zmapowaną listę

                // --- SEKCJA ZAMÓWIEŃ Z PEŁNYM DOSTĘPEM DO SZCZEGÓŁÓW ---
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

                // --- SEKCJA KAR I PŁATNOŚCI ---
                Fines = finesList,
                TotalFinesAmount = finesList.Sum(f => f.Amount)
            };

            return View(model);
        }

        // ==========================================================
        // 2. LOGOWANIE (GET)
        // ==========================================================
        [HttpGet]
        public IActionResult Login(string? returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // ==========================================================
        // 3. LOGOWANIE (POST)
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public async Task<IActionResult> Login(string login, string haslo, string? returnUrl)
        {
            var user = await _context.Users
                .Include(u => u.Klient)
                .FirstOrDefaultAsync(u => u.Login == login); 

            if (user != null && BCrypt.Net.BCrypt.Verify(haslo, user.Haslo))
            {
                HttpContext.Session.SetString("User", user.Login ?? "Użytkownik");
                HttpContext.Session.SetString("UserRole", user.Rola ?? "Klient");

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
                
                return RedirectToAction("Index", "Home");
            }
            ViewBag.Error = "Nieprawidłowy login lub hasło!";
            return View();
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
        // 5. REJESTRACJA (POST)
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken] 
        public IActionResult Register([Bind("Login,Haslo,Email,Imie,Nazwisko,Telefon")] RegisterViewModel model)
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

            return RedirectToAction("Login");
        }

        // ==========================================================
        // 6. WYLOGOWANIE (GET)
        // ==========================================================
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }
    }
}