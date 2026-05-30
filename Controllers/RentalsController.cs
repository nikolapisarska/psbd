using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace meow.Controllers
{
  
    public class RentalsController : Controller
    {
        private readonly LibraryDbContext _context;

        public RentalsController(LibraryDbContext context)
        {
            _context = context;
        }

        // ==========================================================
        // 1. FORMULARZ METODY GET (DODAWANIE WYPOŻYCZENIA PRZEZ ADMINA)
        // ==========================================================
        [HttpGet]
        [MeowAuthorize("Admin")] 
        public IActionResult Create(int? bookId)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("Login", "Account");

            ViewBag.Klienci = _context.Klienci.OrderBy(k => k.Nazwisko).ToList();

            var wypozyczoneEgzemplarzeIds = _context.Wypozyczenia
                .Where(w => w.DataZwrotu == null && w.IdEgzemplarz != null)
                .Select(w => w.IdEgzemplarz)
                .ToList();

            var dostepneQuery = _context.Egzemplarze
                .Include(e => e.Book)
                .Where(e => !wypozyczoneEgzemplarzeIds.Contains(e.IdEgzemplarza));

            if (bookId.HasValue)
            {
                var wybraneId = bookId.Value;
                ViewBag.SelectedBookId = wybraneId;
            }

            ViewBag.DostepneEgzemplarze = dostepneQuery.ToList();

            return View();
        }

        // ==========================================================
        // 2. ZAPIS FORMULARZA POST (MANUALNA REJESTRACJA WYDANIA - ADMIN)
        // ==========================================================
        [HttpPost]
        [MeowAuthorize("Admin")] 
        public IActionResult Create(int id_klient, int id_egzemplarz, DateTime data_wypozyczenia)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("Login", "Account");

            if (data_wypozyczenia > DateTime.Today)
            {
                TempData["Message"] = "Błąd: Data wypożyczenia nie może być z przyszłości!";
                TempData["MessageType"] = "error";
                return RedirectToAction("Create");
            }

            DateTime dataPlanowana = data_wypozyczenia.AddDays(14);

            var wypozyczenie = new Wypozyczenie
            {
                IdKlient = id_klient,
                IdEgzemplarz = id_egzemplarz,
                DataWypozyczenia = data_wypozyczenia,
                DataPlanowanegoZwrotu = dataPlanowana,
                DataZwrotu = null
            };

            _context.Wypozyczenia.Add(wypozyczenie);
            _context.SaveChanges();

            TempData["Message"] = "Wypożyczenie zostało pomyślnie zarejestrowane w bazie meow! 🐾";
            TempData["MessageType"] = "success";
            return RedirectToAction("Returns");
        }

        // ==========================================================
        // 3. SPIS AKTYWNYCH WYPOŻYCZEŃ BIBLIOTECZNYCH I HISTORIA ZAMÓWIEŃ SKLEPU
        // ==========================================================
        [HttpGet]
        [MeowAuthorize("Admin")] 
        public IActionResult Returns()
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin") 
                return RedirectToAction("Login", "Account");

            var listaWypozyczen = _context.Wypozyczenia
                .Include(w => w.Klient)
                .Include(w => w.Egzemplarz).ThenInclude(e => e!.Book)
                .Where(w => w.DataZwrotu == null && w.IdEgzemplarz != null)
                .OrderByDescending(w => w.DataWypozyczenia)
                .ToList();

            var suroweZamowieniaSklepowe = _context.Zamowienia
                .Include(z => z.Klient)
                .Include(z => z.Book)
                .ToList();

            var historiaZamowienDlaAdmina = suroweZamowieniaSklepowe
                .GroupBy(z => z.NumerSledzenia ?? Guid.NewGuid().ToString())
                .Select(g => {
                    var pierwsze = g.First();
                    
                    var pozycjeWZamowieniu = g
                        .Where(z => z.Book != null)
                        .GroupBy(z => z.IdKsiazki)
                        .Select(bGroup => new {
                            Tytul = bGroup.First().Book!.Tytul,
                            Autor = bGroup.First().Book!.Autor,
                            Cena = bGroup.First().Book!.Cena ?? 0.00m,
                            Ilosc = bGroup.Count()
                        }).ToList();

                    decimal lacznaWartosc = pozycjeWZamowieniu.Sum(p => p.Cena * p.Ilosc);

                    string skrótZakupów = string.Join(", ", pozycjeWZamowieniu.Select(p => $"„{p.Tytul}” ({p.Ilosc} szt.)"));

                    return new {
                        OrderId = pierwsze.Id,
                        KlientNazwa = pierwsze.Klient != null ? $"{pierwsze.Klient.Imie} {pierwsze.Klient.Nazwisko}" : "Klient Sklepowy",
                        KlientEmail = pierwsze.Klient?.Email ?? "-",
                        KlientTelefon = pierwsze.Klient?.Telefon ?? "-",
                        DataZlozenia = pierwsze.DataZamowienia.ToString("yyyy-MM-dd HH:mm"),
                        Status = pierwsze.Status,
                        TrackingNumber = pierwsze.NumerSledzenia ?? "Brak numeru",
                        WartoscZamowienia = lacznaWartosc,
                        OpisPozycji = skrótZakupów,
                        SzczegolyPozycji = pozycjeWZamowieniu 
                    };
                })
                .OrderByDescending(z => z.OrderId)
                .ToList<object>();

            ViewBag.ZamowieniaSklepowe = historiaZamowienDlaAdmina;

            return View(listaWypozyczen);
        }

        // ==========================================================
        // 4. POTWIERDZENIE ODBIORU REZERWACJI (START LICZENIA 30 DNI)
        // ==========================================================
        [HttpPost]
        [MeowAuthorize("Admin")] 
        public IActionResult ZatwierdzOdbior(int id_wypozyczenie)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("Login", "Account");

            var wypozyczenie = _context.Wypozyczenia.Find(id_wypozyczenie);
            if (wypozyczenie == null) return RedirectToAction("Returns");

            wypozyczenie.DataWypozyczenia = DateTime.Today;
            wypozyczenie.DataPlanowanegoZwrotu = DateTime.Today.AddDays(30);

            _context.SaveChanges();

            TempData["Message"] = "Książka została pomyślnie wydana klientowi. Termin zwrotu ustawiono na 30 dni! 🐾";
            TempData["MessageType"] = "success";

            return RedirectToAction("Returns");
        }

        // ==========================================================
        // 5. REZERWACJA ONLINE PRZEZ KLIENTA (Z WYBOREM EGZEMPLARZA)
        // ==========================================================
        [HttpPost]
      
        public IActionResult Zarezerwuj(int idEgzemplarza)
        {
            var egzemplarz = _context.Egzemplarze
                .Include(e => e.Book)
                .FirstOrDefault(e => e.IdEgzemplarza == idEgzemplarza);

            if (egzemplarz == null)
            {
                TempData["Message"] = "Nie odnaleziono wybranego egzemplarza.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Index", "Shop");
            }

            int idKsiazki = egzemplarz.Book?.Id ?? 1;

         
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("User")))
            {
                TempData["Message"] = "Musisz się zalogować, aby zarezerwować tę książkę stacjonarnie.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Login", "Account", new { returnUrl = $"/Shop/Details/{idKsiazki}" });
            }

            int? idKlienta = HttpContext.Session.GetInt32("UserId");
            if (idKlienta == null)
            {
                var domyslnyKlient = _context.Klienci.FirstOrDefault();
                idKlienta = domyslnyKlient?.IdKlienta ?? 1;
            }

            bool maZaleglosci = _context.Wypozyczenia.Any(w => 
                w.IdKlient == idKlienta.Value && 
                w.DataZwrotu == null && 
                DateTime.Now > w.DataPlanowanegoZwrotu);

            if (maZaleglosci)
            {
                TempData["Message"] = "Blokada konta: Posiadasz przetrzymane książki! Zwróć zaległe pozycje w bibliotece, aby móc rezerwować kolejne. 🐾";
                TempData["MessageType"] = "error";
                return RedirectToAction("Details", "Shop", new { id = idKsiazki });
            }

            var czyZajety = _context.Wypozyczenia.Any(w => w.IdEgzemplarz == idEgzemplarza && w.DataZwrotu == null);
            if (czyZajety)
            {
                TempData["Message"] = "Ten konkretny egzemplarz został właśnie zarezerwowany przez kogoś innego. Wybierz inny z listy.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Details", "Shop", new { id = idKsiazki });
            }

            DateTime dataNaOdbior = DateTime.Today.AddDays(3);

            var nowaRezerwacja = new Wypozyczenie
            {
                IdKlient = idKlienta.Value,
                IdEgzemplarz = idEgzemplarza,
                IdKsiazki = idKsiazki, 
                DataWypozyczenia = DateTime.Today,
                DataPlanowanegoZwrotu = dataNaOdbior, 
                DataZwrotu = null
            };

            _context.Wypozyczenia.Add(nowaRezerwacja);
            _context.SaveChanges();

            TempData["Message"] = $"🐾 Sukces! Egzemplarz (#{egzemplarz.NumerInwentarzowy}) został zarezerwowany. Zapraszamy po odbiór do dnia: {dataNaOdbior.ToString("dd.MM.yyyy")} r. do godziny 18:00.";
            TempData["MessageType"] = "success";

            return RedirectToAction("Details", "Shop", new { id = idKsiazki });
        }

        // ==========================================================
        // 6. OBSŁUGA ZWROTÓW (URZĘDNIK) + NALICZANIE KAR
        // ==========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [MeowAuthorize("Admin")]
        public IActionResult ZwrocKsiazke(int id_wypozyczenie, DateTime data_zwrotu)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("Login", "Account");

            var wypozyczenie = _context.Wypozyczenia.Find(id_wypozyczenie);
            if (wypozyczenie == null)
                return RedirectToAction("Returns");

            if (data_zwrotu < wypozyczenie.DataWypozyczenia)
            {
                TempData["Message"] = "Błąd: Data zwrotu nie może być wcześniejsza niż data wydania!";
                TempData["MessageType"] = "error";
                return RedirectToAction("Returns");
            }

            wypozyczenie.DataZwrotu = data_zwrotu;

            if (data_zwrotu.Date > wypozyczenie.DataPlanowanegoZwrotu.Date)
            {
                int dniSpoznienia = (data_zwrotu.Date - wypozyczenie.DataPlanowanegoZwrotu.Date).Days;
                decimal kara = dniSpoznienia * 0.50m;

                var platnosc = new Platnosc
                {
                    IdWypozyczenie = id_wypozyczenie,
                    Kwota = kara
                };

                _context.Platnosci.Add(platnosc);
                _context.SaveChanges();

                TempData["Message"] = $"Zwrot zarejestrowany. Naliczono opłatę za {dniSpoznienia} dni spóźnienia w wysokości: {kara:F2} zł.";
                TempData["MessageType"] = "error";
            }
            else
            {
                _context.SaveChanges();
                TempData["Message"] = "Książka zwrócona w terminie. Brak opłat karnych! ✔";
                TempData["MessageType"] = "success";
            }

            return RedirectToAction("Returns");
        }

        // ==========================================================
        // 7. ZWROT REKOMENDOWANY DLA LINKÓW ZEWNĘTRZNYCH
        // ==========================================================
        [HttpGet]
        [MeowAuthorize("Admin")]
        public IActionResult Orders()
        {
            return RedirectToAction("Returns");
        }

        // ==========================================================
        // 8. ZMIANA STATUSU: ZATWIERDZENIE WYSYŁKI PACZKI SKLEPOWEJ
        // ==========================================================
        [HttpPost]
        [MeowAuthorize("Admin")] 
        public IActionResult ZatwierdzWysylke(string trackingNumber)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrEmpty(trackingNumber))
            {
                TempData["Message"] = "Błąd: Brak numeru śledzenia paczki.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Returns");
            }

            var calaPaczka = _context.Zamowienia
                .Where(z => z.NumerSledzenia == trackingNumber)
                .ToList();

            if (!calaPaczka.Any())
            {
                TempData["Message"] = "Nie odnaleziono paczki o podanym numerze śledzenia.";
                TempData["MessageType"] = "error";
                return RedirectToAction("Returns");
            }

            foreach (var item in calaPaczka)
            {
                item.Status = "Wysłana";
            }

            _context.SaveChanges();

            TempData["Message"] = $"Status zaktualizowany: Zbiorcza paczka {trackingNumber} została przekazana kurierowi! 📦🐾";
            TempData["MessageType"] = "success";

            return RedirectToAction("Returns");
        }
    }
}