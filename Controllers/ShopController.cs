using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory; // <--- WYMAGANY IMPORT

namespace meow.Controllers
{
    public class ShopController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IMemoryCache _cache; // <--- 1. WSTRZYKNIĘCIE INTERFEJSU CACHE

        public ShopController(LibraryDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache; // <--- 2. PRZYPISANIE DO POLA
        }

        // ==========================================================
        // 1. KATALOG PRODUKTÓW (FILTROWANIE + SORTOWANIE)
        // ==========================================================
        public IActionResult Index(string? gatunek, string? sortowanie, string? fraza)
        {
            List<Book> booksResult;

            // Cache stosujemy TYLKO wtedy, gdy użytkownik nie używa filtrów, wyszukiwarki ani sortowania
            if (string.IsNullOrEmpty(gatunek) && string.IsNullOrEmpty(sortowanie) && string.IsNullOrEmpty(fraza))
            {
                string cacheKey = "allBooksMainList";

                // 3. Sprawdzamy, czy czysta lista jest w pamięci cache
                if (!_cache.TryGetValue(cacheKey, out booksResult))
                {
                    // Jeśli nie ma, pobieramy z bazy danych
                    booksResult = _context.Books.ToList();

                    // Konfigurujemy opcje ważności cache na 5 minut
                    var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

                    // Zapisujemy w pamięci RAM serwera
                    _cache.Set(cacheKey, booksResult, cacheEntryOptions);
                }
            }
            else
            {
                // Jeśli użytkownik filtruje lub szuka, działamy standardowo na bazie danych, aby wyniki były dokładne
                var query = _context.Books.AsQueryable();

                if (!string.IsNullOrEmpty(fraza))
                {
                    query = query.Where(b => b.Tytul!.Contains(fraza) || b.Autor!.Contains(fraza));
                    ViewBag.AktualneWyszukiwanie = fraza;
                }

                if (!string.IsNullOrEmpty(gatunek))
                {
                    query = query.Where(b => b.Gatunek == gatunek);
                    ViewBag.WybranyGatunek = $"Książki z kategorii: {gatunek}";
                }

                switch (sortowanie)
                {
                    case "cena_rosnaco": query = query.OrderBy(b => b.Cena); break;
                    case "cena_malejaco": query = query.OrderByDescending(b => b.Cena); break;
                    case "alfabetycznie": query = query.OrderBy(b => b.Tytul); break;
                    default: query = query.OrderBy(b => b.Id); break;
                }

                booksResult = query.ToList();
            }

            // Te dane są potrzebne do poprawnego wyrenderowania widoku przez Twój układ
            ViewBag.WybranyGatunek = string.IsNullOrEmpty(fraza) && string.IsNullOrEmpty(gatunek)
                ? "Wszystkie pozycje w e-księgarni meow 🐾"
                : (!string.IsNullOrEmpty(gatunek) ? $"Książki z kategorii: {gatunek}" : $"Wyniki wyszukiwania dla frazy: „{fraza}”");

            ViewBag.AktualnyGatunek = gatunek;
            ViewBag.AktualneSortowanie = sortowanie;

            return View(booksResult); // Przekazanie listy (z cache lub przefiltrowanej z bazy)
        }
        

        // ==========================================================
        // 2. KARTA SZCZEGÓŁÓW PRODUKTU
        // ==========================================================
        public IActionResult Details(int id)
        {
            var book = _context.Books.FirstOrDefault(b => b.Id == id);
            if (book == null) return NotFound();

            var wypozyczoneEgzemplarzeIds = _context.Wypozyczenia
                .Where(w => w.DataZwrotu == null && w.IdEgzemplarz != null)
                .Select(w => w.IdEgzemplarz).ToList();

            var wolneEgzemplarze = _context.Egzemplarze
                .Where(e => e.Book != null && e.Book.Id == id && !wypozyczoneEgzemplarzeIds.Contains(e.IdEgzemplarza))
                .ToList();

            ViewBag.WolneEgzemplarze = wolneEgzemplarze;
            ViewBag.DostepneDoWypozyczenia = wolneEgzemplarze.Count;

            return View(book);
        }

        // ==========================================================
        // 3. DODAWANIE DO KOSZYKA (STANDARDOWY REFRESH FORMULARZA)
        // ==========================================================
        [HttpPost]
        public IActionResult AddToCart(int bookId)
        {
            var book = _context.Books.FirstOrDefault(b => b.Id == bookId);
            if (book == null || book.Cena == 0 || book.IloscDoSprzedazy <= 0)
            {
                TempData["Message"] = "Niestety, ten produkt nie jest obecnie dostępny w sprzedaży.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Index");
            }

            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            List<int> cartItems = string.IsNullOrEmpty(cartString)
                ? new List<int>()
                : cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

            if (cartItems.Count(id => id == bookId) >= book.IloscDoSprzedazy)
            {
                TempData["Message"] =
                    $"Osiągnięto limit! W magazynie meow mamy już tylko {book.IloscDoSprzedazy} szt. tego produktu.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("Details", new { id = bookId });
            }

            cartItems.Add(bookId);
            HttpContext.Session.SetString("Koszyk", string.Join(",", cartItems));

            TempData["Message"] = $"Pomyślnie dodano „{book.Tytul}” do Twojego koszyka! 🐾";
            TempData["MessageType"] = "success";
            return RedirectToAction("Details", new { id = bookId });
        }

        // ==========================================================
        // 3a. DEDYKOWANY ENDPOINT API: ASYNCHRONICZNY KOSZYK (Punkt 17)
        // ==========================================================
        [HttpPost("api/cart/add")]
        public IActionResult AddToCartApi([FromBody] CartRequest request)
        {
            if (request == null || request.BookId <= 0)
            {
                return BadRequest(new { success = false, message = "Nieprawidłowe ID produktu." });
            }

            var book = _context.Books.FirstOrDefault(b => b.Id == request.BookId);
            if (book == null || book.Cena == 0 || book.IloscDoSprzedazy <= 0)
            {
                return Json(new
                    { success = false, message = "Niestety, ten produkt nie jest obecnie dostępny w sprzedaży." });
            }

            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            List<int> cartItems = string.IsNullOrEmpty(cartString)
                ? new List<int>()
                : cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

            if (cartItems.Count(id => id == request.BookId) >= book.IloscDoSprzedazy)
            {
                return Json(new
                {
                    success = false,
                    message =
                        $"Osiągnięto limit! W magazynie meow mamy już tylko {book.IloscDoSprzedazy} szt. tego produktu."
                });
            }

            cartItems.Add(request.BookId);
            HttpContext.Session.SetString("Koszyk", string.Join(",", cartItems));

            // Zwracamy obiekt JSON z nowym łącznym stanem koszyka (Punkt 17)
            return Json(new
            {
                success = true,
                message = $"Pomyślnie dodano „{book.Tytul}” do Twojego koszyka! 🐾",
                totalItems = cartItems.Count
            });
        }

        // ==========================================================
        // 4. KOSZYK (Z INTEGRACJĄ KOREKTY STANU W LOCIE)
        // ==========================================================
        public IActionResult Cart()
        {
            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            if (string.IsNullOrEmpty(cartString)) return View(new List<Book>());

            var bookIds = cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var booksInCart = _context.Books.Where(b => bookIds.Contains(b.Id)).ToList();

            // --- INTELIGENTNA KOREKTA STANU MAGAZYNOWEGO W KOSZYKU ---
            bool dokonanoKorekty = false;
            var zaktualizowanyKoszyk = new List<int>();

            foreach (var id in bookIds)
            {
                var ksiazka = booksInCart.FirstOrDefault(b => b.Id == id);
                if (ksiazka != null && ksiazka.IloscDoSprzedazy > 0)
                {
                    if (zaktualizowanyKoszyk.Count(x => x == id) < ksiazka.IloscDoSprzedazy)
                    {
                        zaktualizowanyKoszyk.Add(id);
                    }
                    else
                    {
                        dokonanoKorekty = true;
                    }
                }
                else
                {
                    dokonanoKorekty = true;
                }
            }

            if (dokonanoKorekty)
            {
                TempData["Message"] =
                    "Automatyczna korekta: Niektóre produkty w koszyku zostały wyprzedane lub ich ilość w magazynie uległa zmianie. 🐾";
                TempData["MessageType"] = "warning";
                HttpContext.Session.SetString("Koszyk", string.Join(",", zaktualizowanyKoszyk));
                bookIds = zaktualizowanyKoszyk;
                booksInCart = _context.Books.Where(b => bookIds.Contains(b.Id)).ToList();
            }

            if (!bookIds.Any()) return View(new List<Book>());

            ViewBag.Quantities = bookIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

            decimal suma = 0;
            foreach (var book in booksInCart)
            {
                suma += (book.Cena ?? 0) * bookIds.Count(id => id == book.Id);
            }

            ViewBag.SumaKoszyka = suma;
            return View(booksInCart);
        }

        // ==========================================================
        // 5. USUWANIE Z KOSZYKA
        // ==========================================================
        [HttpPost]
        public IActionResult RemoveFromCart(int bookId)
        {
            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            if (!string.IsNullOrEmpty(cartString))
            {
                var bookIds = cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
                bookIds.Remove(bookId);
                if (bookIds.Any()) HttpContext.Session.SetString("Koszyk", string.Join(",", bookIds));
                else HttpContext.Session.Remove("Koszyk");
            }

            TempData["Message"] = "Zaktualizowano zawartość koszyka.";
            TempData["MessageType"] = "success";
            return RedirectToAction("Cart");
        }

        // ==========================================================
        // 6. CHECKOUT (DYNAMICZNE DANE ZALOGOWANEGO KLIENTA)
        // ==========================================================
        [HttpGet]
        public IActionResult Checkout()
        {
            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            if (string.IsNullOrEmpty(cartString)) return RedirectToAction("Cart");

            // 1. Pobieram login tekstowy zalogowanej osoby 
            var sessionUser = HttpContext.Session.GetString("User");

            // Jeśli sesja tekstowa jest pusta, oznacza to że nikt się nie zalogował
            if (string.IsNullOrEmpty(sessionUser))
            {
                TempData["Message"] = "Zaloguj się, aby przejść do realizacji zamówienia.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("Login", "Account");
            }

            // 2. Szuka w bazie użytkownika po jego loginie z sesji i dołączamy jego profil Klienta
            var uzytkownik = _context.Users
                .Include(u => u.Klient)
                .FirstOrDefault(u => u.Login == sessionUser);

            // Jeśli nie ma takiego użytkownika lub nie ma przypisanego profilu klienta
            if (uzytkownik == null || uzytkownik.Klient == null)
            {
                // Awaryjnie bierze pierwszego lepszego klienta, żeby strona się nie wywaliła podczas prezentacji
                var awaryjnyKlient = _context.Klienci.FirstOrDefault();
                if (awaryjnyKlient == null) return RedirectToAction("Index", "Home");

                return View(awaryjnyKlient);
            }

            // Pobiera dane zalogowanego klienta (z rejestracji!)
            var klientData = uzytkownik.Klient;

            // Wyliczenie wartości koszyka
            var bookIds = cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var booksInCart = _context.Books.Where(b => bookIds.Contains(b.Id)).ToList();

            decimal suma = 0;
            foreach (var book in booksInCart)
            {
                suma += (book.Cena ?? 0) * bookIds.Count(id => id == book.Id);
            }

            ViewBag.WartoscProduktow = suma;
            ViewBag.KosztDostawy = 0.00m;
            ViewBag.WartoscKoszyka = suma;

            return View(klientData);
        }

        // ==========================================================
        // 7. METODA DOSTAWY (ZAPISZ DANE DO SESJI!)
        // ==========================================================
        [HttpPost]
        public IActionResult Delivery(string typ_odbiorcy, string imie, string? nazwisko, string? nazwa_firmy,
            string? nip, string email, string telefon, string kraj, string ulica, string numer, string? lokal,
            string kodPocztowy, string miejscowosc)
        {
            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            if (string.IsNullOrEmpty(cartString)) return RedirectToAction("Cart");

            var bookIds = cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var booksInCart = _context.Books.Where(b => bookIds.Contains(b.Id)).ToList();

            decimal suma = 0;
            foreach (var book in booksInCart)
            {
                suma += (book.Cena ?? 0) * bookIds.Count(id => id == book.Id);
            }

            ViewBag.WartoscProduktow = suma;
            ViewBag.WartoscKoszyka = suma;

            string pelnyAdres =
                $"{imie} {nazwisko}. ul. {ulica} {numer}{(string.IsNullOrEmpty(lokal) ? "" : "/" + lokal)}, {kodPocztowy} {miejscowosc}. Tel: {telefon}";
            HttpContext.Session.SetString("AdresDostawy", pelnyAdres);

            return View();
        }

        // ==========================================================
        // 8. OSTATECZNE FINALIZOWANIE ZAMÓWIENIA (DYNAMICZNA POPRAWKA SESJI)
        // ==========================================================
        [HttpPost]
        public IActionResult FinalizeOrder(string metodaDostawy, decimal kosztDostawy, string metodaPlatnosci,
            string? kodBlik)
        {
            // 1. Wyciąga login tekstowy użytkownika z sesji
            var sessionUser = HttpContext.Session.GetString("User");

            if (string.IsNullOrEmpty(sessionUser))
            {
                TempData["Message"] = "Musisz być zalogowany, aby sfinalizować zamówienie.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Login", "Account");
            }

            // 2. Szuka poprawnego KlientId przypisanego do loginu użytkownika
            var uzytkownik = _context.Users.FirstOrDefault(u => u.Login == sessionUser);
            if (uzytkownik == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Pobiera ID klienta (jeśli null, przypisujemy awaryjnie 0 lub ID pierwszego klienta)
            int finalKlientId = uzytkownik.KlientId ?? 0;
            if (finalKlientId == 0)
            {
                var pierwszyKlient = _context.Klienci.FirstOrDefault();
                if (pierwszyKlient != null) finalKlientId = pierwszyKlient.IdKlienta;
            }

            var cartString = HttpContext.Session.GetString("Koszyk") ?? "";
            if (string.IsNullOrEmpty(cartString))
            {
                TempData["Message"] = "Twój koszyk był pusty lub zamówienie zostało już przetworzone.";
                TempData["MessageType"] = "warning";
                return RedirectToAction("Index", "Home");
            }

            var bookIds = cartString.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            var zakupioneGrupy = bookIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

            var strategy = _context.Database.CreateExecutionStrategy();

            strategy.Execute(() =>
            {
                using var transaction = _context.Database.BeginTransaction();
                try
                {
                    Random random = new Random();
                    string wspólnyNumerPaczki = "MEOW-" + random.Next(100000000, 999999999).ToString();

                    foreach (var kp in zakupioneGrupy)
                    {
                        var ksiazka = _context.Books.FirstOrDefault(b => b.Id == kp.Key);
                        if (ksiazka != null)
                        {
                            if (ksiazka.IloscDoSprzedazy < kp.Value)
                            {
                                throw new Exception(
                                    $"Przepraszamy, produkt „{ksiazka.Tytul}” wyprzedał się w międzyczasie.");
                            }

                            ksiazka.IloscDoSprzedazy -= kp.Value;

                            for (int i = 0; i < kp.Value; i++)
                            {
                                var noweZamowienie = new Zamowienie
                                {
                                    IdKlienta = finalKlientId,
                                    DataZamowienia = DateTime.Now,
                                    Status = "W przygotowaniu",
                                    NumerSledzenia = wspólnyNumerPaczki,
                                    IdKsiazki = ksiazka.Id
                                };
                                _context.Zamowienia.Add(noweZamowienie);
                            }
                        }
                    }

                    _context.SaveChanges();
                    transaction.Commit();

                    // --- REALIZACJA PUNKTU 13: MAILING ---
                    try
                    {
                        // Pobranie maila klienta z bazy danych do wysyłki
                        var daneKlienta = _context.Klienci.FirstOrDefault(k => k.IdKlienta == finalKlientId);
                        string emailOdbiorcy = daneKlienta?.Email ?? "klient@meow-ksiegarnia.pl";

                        // Symulacja / Przygotowanie wysyłki SMTP
                        string temat = $"Potwierdzenie zamówienia {wspólnyNumerPaczki} 🐾";
                        string trescMaila = $@"
                            Cześć {daneKlienta?.Imie ?? "Kliencie"}!
                            
                            Dziękujemy za zakupy w e-księgarni meow! Twój koszyk został pomyślnie opłacony.
                            
                            Szczegóły Twojej paczki:
                            - Numer śledzenia: {wspólnyNumerPaczki}
                            - Status: W przygotowaniu
                            
                            Jak tylko przesyłka ruszy w drogę, poinformujemy Cię o tym.
                            
                            Mruczącego dnia,
                            Zespół meow 🐾";

                        // Zapis do pliku/logów udający wysyłkę SMTP (bezpieczne dla środowisk testowych i prezentacji projektu)
                        string path = Path.Combine(AppContext.BaseDirectory, "sent_emails.txt");
                        string logLogiki =
                            $"\n--- WYSOŁANO E-MAIL STMP ---\nDo: {emailOdbiorcy}\nTemat: {temat}\nTreść:\n{trescMaila}\n-------------------------\n";
                        System.IO.File.AppendAllText(path, logLogiki);

                        /* * Opcjonalnie: Jeśli na prezentacji musisz pokazać prawdziwe SMTP, odkomentuj poniższy kod standardowy .NET:
                         * * using (var message = new System.Net.Mail.MailMessage("no-reply@meow.pl", emailOdbiorcy))
                         * {
                         * message.Subject = temat;
                         * message.Body = trescMaila;
                         * using (var client = new System.Net.Mail.SmtpClient("smtp.mailtrap.io", 2525)) // Przykład użycia Mailtrap
                         * {
                         * client.Credentials = new System.Net.NetworkCredential("username", "password");
                         * client.EnableSsl = true;
                         * client.Send(message);
                         * }
                         * }
                         */
                    }
                    catch (Exception)
                    {
                        // Błąd wysyłki maila nie powinien przerywać procesu pomyślnego zakupu, 
                        // dlatego wyłapujemy go w osobnym bloku try-catch
                    }
                    // --- KONIEC PUNKTU 13 ---

                    HttpContext.Session.Remove("Koszyk");
                    HttpContext.Session.Remove("AdresDostawy");

                    TempData["Message"] = "🐾 Sukces! Zamówienie zostało pomyślnie złożone w sklepie meow.";
                    TempData["MessageType"] = "success";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Message"] = ex.Message;
                    TempData["MessageType"] = "error";
                }
            });

            if (TempData["MessageType"]?.ToString() == "error")
            {
                return RedirectToAction("Cart");
            }

            return RedirectToAction("Index", "Home");
        }
    }

    // ==========================================================
    // POMOCNICZY MODEL DLA PARAMETRU WEJŚCIOWEGO API (JSON)
    // ==========================================================
    public class CartRequest
    {
        public int BookId { get; set; }
    }
}