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


        public HomeController(LibraryDbContext context)
        {
            _context = context;
        }

        // ==========================================================
        // INTELIGENTNA STRONA GŁÓWNA: ROZDZIELENIE SKLEPU OD ADMINA 
        // ==========================================================
        public async Task<IActionResult> Index()
        {
            var sessionRole = HttpContext.Session.GetString("UserRole");

        
            if (sessionRole == "Admin")
            {
            
                ViewBag.LiczbaKsiazek = await _context.Books.CountAsync();
                ViewBag.LiczbaEgzemplarzy = await _context.Egzemplarze.CountAsync();
                ViewBag.AktywneWypozyczenia = await _context.Wypozyczenia.CountAsync(w => w.DataZwrotu == null && w.IdEgzemplarz != null);
                ViewBag.NoweZamowieniaSklep = await _context.Wypozyczenia.CountAsync(w => w.DataZwrotu == null && w.IdEgzemplarz == null);
                ViewBag.LiczbaKlientow = await _context.Klienci.CountAsync();

                return View("AdminDashboard"); 
            }

            var nowosci = await _context.Books.OrderByDescending(b => b.Id).Take(4).ToListAsync();

            return View(nowosci);
        }

        public IActionResult Struktura()
        {
            return View();
        }
    }
}