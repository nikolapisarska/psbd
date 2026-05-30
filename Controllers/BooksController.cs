using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;

namespace meow.Controllers
{
    [MeowAuthorize("Admin")]
    public class BooksController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly string[] _wszystkieGatunki = new[] { 
            "Biografia", "Biznes", "Ezoteryka i parapsychologia", "Fantasy", "Historia", 
            "Komiksy, Mangy", "Kryminał", "Dla dzieci", "Dla młodzieży", "Kuchnia i diety",
            "Popularnonaukowa", "Literatura obcojęzyczna", "Literatura obyczajowa", "Powieść", 
            "Nauka języków", "Nauki humanistyczne", "Naukowe", "Podręczniki akademickie", 
            "Podróże i turystyka", "Poezja", "Poradniki", "Prawo", "Religia", "Sport",
            "Wiek-0-2", "Wiek-3-5", "Wiek-6-8", "Wiek-9-12", "Emocje", "Kariera", "Psychologia"
        };

        public BooksController(LibraryDbContext context, IWebHostEnvironment webHostEnvironment) 
        { 
            _context = context; 
            _webHostEnvironment = webHostEnvironment;
        }

        // ==========================================================
        // 1. WIDOK GŁÓWNY PANELU ADMINISTRATORA
        // ==========================================================
        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("User") == null) return RedirectToAction("Login", "Account");

            var books = _context.Books.Include(b => b.Egzemplarze).ToList();
            
            ViewBag.AktywneWypozyczenia = _context.Wypozyczenia
                .Where(w => w.DataZwrotu == null && w.IdEgzemplarz != null)
                .Select(w => (int)w.IdEgzemplarz!)
                .ToList();

            ViewBag.Gatunki = _wszystkieGatunki;
            ViewBag.Stany = new[] { "idealny", "bardzo dobry", "dobry", "zużyty", "zniszczony" };

            return View(books);
        }

        // ==========================================================
        // 2. OTWARCIE FORMULARZA TWORZENIA (ŻĄDANIE GET)
        // ==========================================================
        [HttpGet]
        public IActionResult Create()
        {
            if (HttpContext.Session.GetString("User") == null) return RedirectToAction("Login", "Account");
            return View();
        }

        // ==========================================================
        // 3. ZAPIS FORMULARZA DO BAZY (ŻĄDANIE POST)
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Book book, int iloscBiblioteka, string[] stany, IFormFile zdjecieOkładki)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            strategy.Execute(() =>
            {
                using var transaction = _context.Database.BeginTransaction();
                try
                {
                    if (zdjecieOkładki != null && zdjecieOkładki.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(zdjecieOkładki.FileName);
                        var folderPath = Path.Combine(_webHostEnvironment.WebRootPath, "images");
                        
                        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                        var filePath = Path.Combine(folderPath, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            zdjecieOkładki.CopyTo(stream);
                        }

                        book.ImageUrl = "/images/" + fileName;
                    }

                    _context.Books.Add(book);
                    _context.SaveChanges();

                    if (iloscBiblioteka > 0)
                    {
                        for (int i = 0; i < iloscBiblioteka; i++)
                        {
                            string stan = (stany != null && stany.Length > i) ? stany[i] : "idealny";
                            string nrInw = $"INV-{DateTime.Now.Year}-{book.Id}-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";

                            var egz = new Egzemplarz
                            {
                                IdKsiazka = book.Id,
                                NumerInwentarzowy = nrInw,
                                Stan = stan
                            };
                            _context.Egzemplarze.Add(egz);
                        }
                    }

                    book.IloscEgzemplarzy = iloscBiblioteka;
                    _context.SaveChanges();
                    transaction.Commit();

                    TempData["Message"] = $"Produkt '{book.Tytul}' został dodany do systemu meow! 🐾";
                    TempData["MessageType"] = "success";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Message"] = "Błąd zapisu produktu: " + ex.Message;
                    TempData["MessageType"] = "error";
                }
            });

            return RedirectToAction("Index");
        }

        // ==========================================================
        // 4. OTWARCIE FORMULARZA EDYCJI (ŻĄDANIE GET) - BRAKUJĄCY KOD REGENEROWANY!
        // ==========================================================
        [HttpGet]
        [Route("Books/Edit/{id}")]
        public IActionResult Edit(int id)
        {
            if (HttpContext.Session.GetString("User") == null) return RedirectToAction("Login", "Account");
            
            var book = _context.Books.FirstOrDefault(b => b.Id == id);
            if (book == null) return NotFound();

            ViewBag.Gatunki = _wszystkieGatunki;
            return View(book);
        }

        // ==========================================================
        // 5. ZARZĄDZANIE OFERTĄ SKLEPU (ŻĄDANIE POST Z TRASĄ AWARYJNĄ)
        // ==========================================================
        [HttpPost]
        [Route("Books/Edit/{id?}")] // Akceptuje adresy zarówno czyste, jak i z końcówką /1
        public IActionResult Edit(int? id, Book updatedBook, string opis, IFormFile? noweZdjecie)
        {
            // Przypisanie ID, jeśli przyszło z adresu URL zamiast ukrytego pola
            if ((updatedBook.Id == 0 || updatedBook.Id == null) && id.HasValue)
            {
                updatedBook.Id = id.Value;
            }

            var strategy = _context.Database.CreateExecutionStrategy();

            strategy.Execute(() =>
            {
                using var transaction = _context.Database.BeginTransaction();
                try
                {
                    var bookInDb = _context.Books.FirstOrDefault(b => b.Id == updatedBook.Id);
                    if (bookInDb == null) return;

                    bookInDb.Tytul = updatedBook.Tytul;
                    bookInDb.Autor = updatedBook.Autor;
                    bookInDb.Gatunek = updatedBook.Gatunek;
                    bookInDb.RokWydania = updatedBook.RokWydania;
                    bookInDb.IloscDoSprzedazy = updatedBook.IloscDoSprzedazy;
                    bookInDb.Opis = opis;

                    if (Request.Form.ContainsKey("cenaSklep"))
                    {
                        string cenaRaw = Request.Form["cenaSklep"].ToString().Replace(",", ".");
                        if (decimal.TryParse(cenaRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal cenaParsed))
                        {
                            bookInDb.Cena = cenaParsed;
                        }
                    }
                    
                    if (Request.Form.ContainsKey("cenaOkladkaPub"))
                    {
                        string cenaOklRaw = Request.Form["cenaOkladkaPub"].ToString().Replace(",", ".");
                        if (decimal.TryParse(cenaOklRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal cenaOklParsed))
                        {
                            bookInDb.CenaOkladkowa = cenaOklParsed;
                        }
                    }

                    bookInDb.Wydawnictwo = updatedBook.Wydawnictwo;
                    bookInDb.LiczbaStron = updatedBook.LiczbaStron;
                    bookInDb.OkladkaTyp = updatedBook.OkladkaTyp;
                    bookInDb.Tlumaczenie = updatedBook.Tlumaczenie;
                    bookInDb.EAN = updatedBook.EAN;
                    
                    bookInDb.TytulOryginalny = updatedBook.TytulOryginalny;
                    bookInDb.Seria = updatedBook.Seria;
                    bookInDb.JezykWydania = updatedBook.JezykWydania;
                    bookInDb.JezykOryginalu = updatedBook.JezykOryginalu;
                    bookInDb.NumerWydania = updatedBook.NumerWydania;
                    bookInDb.DataPremiery = updatedBook.DataPremiery;
                    bookInDb.DataWydania = updatedBook.DataWydania;
                    bookInDb.WysokoscMm = updatedBook.WysokoscMm;
                    bookInDb.GlebokoscMm = updatedBook.GlebokoscMm;
                    bookInDb.SzerokoscMm = updatedBook.SzerokoscMm;

                    if (noweZdjecie != null && noweZdjecie.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(noweZdjecie.FileName);
                        var folderPath = Path.Combine(_webHostEnvironment.WebRootPath, "images");
                        var filePath = Path.Combine(folderPath, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            noweZdjecie.CopyTo(stream);
                        }

                        bookInDb.ImageUrl = "/images/" + fileName;
                    }

                    _context.SaveChanges();
                    transaction.Commit();
                    
                    TempData["Message"] = $"Zaktualizowano parametry oferty dla '{bookInDb.Tytul}'! 🐾";
                    TempData["MessageType"] = "success";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Message"] = "Błąd edycji zasobu: " + ex.Message;
                    TempData["MessageType"] = "error";
                }
            });

            return RedirectToAction("Index");
        }

        // ==========================================================
        // 6. EDYCJA STANU FIZYCZNEGO EGZEMPLARZA
        // ==========================================================
        [HttpPost]
        public IActionResult ZapiszEgzemplarz(int idEgzemplarza, string stan)
        {
            var egz = _context.Egzemplarze.FirstOrDefault(e => e.IdEgzemplarza == idEgzemplarza);
            if (egz != null)
            {
                egz.Stan = stan; 
                _context.SaveChanges();
                TempData["Message"] = "Stan techniczny egzemplarza został zaktualizowany.";
                TempData["MessageType"] = "success";
            }
            return RedirectToAction("Index");
        }

        // ==========================================================
        // 7. USUWANIE ZASOBÓW
        // ==========================================================
        public IActionResult UsunEgzemplarz(int id)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            strategy.Execute(() =>
            {
                using var transaction = _context.Database.BeginTransaction();
                try
                {
                    var egz = _context.Egzemplarze.Find(id);
                    if (egz == null) return;

                    int idKsiazki = egz.IdKsiazka;
                    bool czyWypozyczony = _context.Wypozyczenia.Any(w => w.IdEgzemplarz == id && w.DataZwrotu == null);

                    if (czyWypozyczony)
                    {
                        TempData["Message"] = "Nie można usunąć: ten egzemplarz jest obecnie wypożyczony przez klienta!";
                        TempData["MessageType"] = "error";
                        return;
                    }

                    var powiazaneWypozyczenia = _context.Wypozyczenia.Where(w => w.IdEgzemplarz == id).ToList();
                    foreach (var w in powiazaneWypozyczenia)
                    {
                        var platnosci = _context.Platnosci.Where(p => p.IdWypozyczenie == w.IdWypozyczenie);
                        _context.Platnosci.RemoveRange(platnosci);
                    }
                    _context.Wypozyczenia.RemoveRange(powiazaneWypozyczenia);
                    _context.Egzemplarze.Remove(egz);
                    _context.SaveChanges();

                    var ksiazka = _context.Books.Find(idKsiazki);
                    if (ksiazka != null)
                    {
                        ksiazka.IloscEgzemplarzy = _context.Egzemplarze.Count(e => e.IdKsiazka == idKsiazki);
                        
                        if (ksiazka.IloscEgzemplarzy == 0 && ksiazka.IloscDoSprzedazy == 0)
                        {
                            _context.Books.Remove(ksiazka);
                        }
                        _context.SaveChanges();
                    }

                    transaction.Commit();
                    TempData["Message"] = "Egzemplarz został pomyślnie usunięty.";
                    TempData["MessageType"] = "success";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Message"] = "Błąd podczas usuwania: " + ex.Message;
                    TempData["MessageType"] = "error";
                }
            });

            return RedirectToAction("Index");
        }
    }
}