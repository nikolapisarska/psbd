using Microsoft.AspNetCore.Mvc;
using meow.Models; // Poprawny import modeli Twojego projektu
using System.Linq;

namespace meow.Controllers // Poprawny namespace Twojego projektu
{
    [ApiController]
    [Route("api/[controller]")]
    public class BooksApiController : ControllerBase
    {
        private readonly LibraryDbContext _context; // Poprawna nazwa Twojego DbContextu

        public BooksApiController(LibraryDbContext context)
        {
            _context = context;
        }

        // Metoda GET: api/booksapi
        [HttpGet]
        public IActionResult Get()
        {
            var books = _context.Books.ToList();
            return Ok(books);
        }
    }
}