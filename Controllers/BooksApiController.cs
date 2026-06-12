using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using meow.Models;
using Microsoft.EntityFrameworkCore;

namespace meow.Controllers
{
    [Route("api/booksapi")]
    public class BooksApiController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LibraryDbContext _context;

        public BooksApiController(IHttpClientFactory httpClientFactory, LibraryDbContext context)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
        }

        [HttpGet]
        public IActionResult GetBooks()
        {
            var books = _context.Books.ToList(); 
            return Json(books); 
        }

        [HttpGet("Index")] 
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost("Search")] 
        public async Task<IActionResult> Search([FromForm] string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Json(new { success = false, message = "Wyszukiwana fraza nie może być pusta." });
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "meow-library-app"); 

            string apiKey = "AIzaSyA7oz_7f1vZCZxFVepWm8ti8cbGhFqa8q0"; 
            
            string url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=10&key={apiKey}";

            try
            {
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("totalItems", out var totalItems) && totalItems.GetInt32() > 0 && root.TryGetProperty("items", out var itemsArray))
                    {
                        var booksList = new List<object>();

                        foreach (var item in itemsArray.EnumerateArray())
                        {
                            if (!item.TryGetProperty("volumeInfo", out var volumeInfo)) continue;

                            var title = volumeInfo.TryGetProperty("title", out var t) ? t.GetString() : "Brak tytułu";
                            var publisher = volumeInfo.TryGetProperty("publisher", out var p) ? p.GetString() : "Nieznane wydawnictwo";
                            var publishedDate = volumeInfo.TryGetProperty("publishedDate", out var pd) ? pd.GetString() : "Brak daty";
                            var description = volumeInfo.TryGetProperty("description", out var d) ? d.GetString() : "Brak opisu";
            
                            var printType = volumeInfo.TryGetProperty("printType", out var pt) ? pt.GetString() : "BOOK";
                            // Zabezpieczenie: jeśli pageCount nie istnieje w API, przypisujemy "0" zamiast błędu parsowania typu int
                            var pageCount = volumeInfo.TryGetProperty("pageCount", out var pc) ? pc.GetInt32().ToString() : "0";
                            var language = volumeInfo.TryGetProperty("language", out var lang) ? lang.GetString() : "pl";

                            var authorsList = new List<string>();
                            if (volumeInfo.TryGetProperty("authors", out var authorsArray))
                            {
                                foreach (var author in authorsArray.EnumerateArray())
                                {
                                    authorsList.Add(author.GetString());
                                }
                            }
                            var authors = string.Join(", ", authorsList);

                            string thumbnailUrl = "";
                            if (volumeInfo.TryGetProperty("imageLinks", out var imageLinks))
                            {
                                thumbnailUrl = imageLinks.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : "";
                                if (!string.IsNullOrEmpty(thumbnailUrl) && thumbnailUrl.StartsWith("http://"))
                                {
                                    thumbnailUrl = thumbnailUrl.Replace("http://", "https://");
                                }
                            }

                            booksList.Add(new { 
                                title, 
                                authors, 
                                publisher, 
                                publishedDate, 
                                description, 
                                thumbnailUrl,
                                printType,    
                                pageCount,    
                                language      
                            });
                        }

                        return Json(new { 
                            success = true, 
                            books = booksList
                        });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Nie znaleziono żadnej książki dla podanego zapytania." });
                    }
                }
                else
                {
                    return Json(new { success = false, message = $"Błąd zewnętrznego API. Status: {response.StatusCode}" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Błąd połączenia z API: " + ex.Message });
            }
        }

       
        [HttpPost("SaveToDatabase")]
public async Task<IActionResult> SaveToDatabase(
    string title, 
    string authors, 
    string publisher, 
    string description, 
    string imageUrl, 
    int pageCount, 
    string language, 
    string printType,
    string publishedDate) // <-- DODANO PARAMETR OD ZEWNĘTRZNEGO API
{
    if (string.IsNullOrEmpty(title))
    {
        return Json(new { success = false, message = "Nie udało się zapisać książki – brak tytułu." });
    }

    try
    {
        // POPRAWKA 1: Prawidłowe wyciąganie roku z publishedDate (np. "2023-05-12" lub "2023")
        int rok = DateTime.Now.Year;
        if (!string.IsNullOrEmpty(publishedDate) && publishedDate.Length >= 4) 
        {
            int.TryParse(publishedDate.Substring(0, 4), out rok);
        }
        
        // Zabezpieczenie na wypadek, gdyby parsowanie dało dziwny rok poniżej dopuszczalnego w formularzu
        if (rok < 1000) rok = DateTime.Now.Year;

        var newBook = new Book
        {
            Tytul = title,
            Autor = string.IsNullOrEmpty(authors) ? "Nieznany autor" : authors,
            Wydawnictwo = string.IsNullOrEmpty(publisher) ? "Nieznane wydawnictwo" : publisher,
            Opis = description?.Length > 1000 ? description.Substring(0, 997) + "..." : description,
            ImageUrl = imageUrl,
            
            // Pola finansowo-magazynowe (gwarantujemy wartości liczbowe dla bazy)
            Cena = 0.00m,                     
            CenaOkladkowa = 0.00m,
            IloscEgzemplarzy = 1, // Domyślnie 1 sztuka na półce biblioteki            
            IloscDoSprzedazy = 0, 
            
            // POPRAWKA 2: Domyślny gatunek, aby walidator 'required' w edycji nie blokował zapisu
            Gatunek = "Powieść", // Lub inna dowolna domyślna wartość z Twojej listy optgroup
            
            JezykWydania = string.IsNullOrEmpty(language) ? "polski" : language.ToLower(),
            NumerWydania = "I",
            LiczbaStron = pageCount > 0 ? pageCount : null,
            OkladkaTyp = printType == "MAGAZINE" ? "Miękka (magazyn)" : "Zwykła",
            RokWydania = rok
        };

        _context.Books.Add(newBook);
        await _context.SaveChangesAsync();

        return Json(new { success = true, message = $"Książka \"{newBook.Tytul}\" została pomyślnie zapisana w bazie systemu meow! 🐾" });
    }
    catch (Exception ex)
    {
        var innerMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        return Json(new { success = false, message = "Błąd podczas zapisu do bazy danych: " + innerMessage });
    }
}
    }
}