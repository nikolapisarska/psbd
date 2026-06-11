using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System.Linq;
using System.Threading.Tasks;

namespace meow.Controllers
{
    public class HomeController : Controller
    {
        private readonly LibraryDbContext _context;

        // Wstrzykiwanie kontekstu bazy danych przez konstruktor
        public HomeController(LibraryDbContext context)
        {
            _context = context;
        }

        // ==========================================================
        // INTELIGENTNA STRONA GŁÓWNA: ROZDZIELENIE SKLEPU OD ADMINA (ASYNC)
        // ==========================================================
        public async Task<IActionResult> Index()
        {
            var sessionRole = HttpContext.Session.GetString("UserRole");

            // Jeśli zalogowany użytkownik ma rolę Admina, pokazujemy mu kokpit zarządczy
            if (sessionRole == "Admin")
            {
                // Pobieramy szybkie statystyki do wyświetlenia na kafelkach dashboardu (asynchronicznie)
                ViewBag.LiczbaKsiazek = await _context.Books.CountAsync();
                ViewBag.LiczbaEgzemplarzy = await _context.Egzemplarze.CountAsync();
                ViewBag.AktywneWypozyczenia = await _context.Wypozyczenia.CountAsync(w => w.DataZwrotu == null && w.IdEgzemplarz != null);
                ViewBag.NoweZamowieniaSklep = await _context.Wypozyczenia.CountAsync(w => w.DataZwrotu == null && w.IdEgzemplarz == null);
                ViewBag.LiczbaKlientow = await _context.Klienci.CountAsync();

                return View("AdminDashboard"); // Wywołujemy dedykowany plik widoku dla admina
            }

            // DLA ZWYKŁEGO UŻYTKOWNIKA / GOŚCIA: Ładujemy nowości do wyświetlenia w HTML
            // Zmieniamy na asynchroniczne ToListAsync(), aby baza danych działała wydajniej
            var nowosci = await _context.Books.OrderByDescending(b => b.Id).Take(4).ToListAsync();
            // Przekazujemy listę książek do widoku Views/Home/Index.cshtml
            return View(nowosci);
        }

        // Zachowujemy akcję Struktura, aby podstrona nie przestała działać
        public IActionResult Struktura()
        {
            return View();
        }
    }
}