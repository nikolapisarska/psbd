using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using meow.Models;

namespace meow.Controllers
{
    public class BooksApiController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LibraryDbContext _context;

        public BooksApiController(IHttpClientFactory httpClientFactory, LibraryDbContext context)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Search(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                ViewBag.Error = "Wyszukiwana fraza lub ISBN nie może być pusta.";
                return View("Index");
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            // KLUCZOWE: Google Books często blokuje żądania bez User-Agent
            client.DefaultRequestHeaders.Add("User-Agent", "meow-library-app"); 

            string url = $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(query)}&maxResults=1";

            try
            {
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("totalItems", out var totalItems) && totalItems.GetInt32() > 0)
                    {
                        var firstBook = root.GetProperty("items")[0].GetProperty("volumeInfo");

                        ViewBag.Title = firstBook.TryGetProperty("title", out var t) ? t.GetString() : "Brak tytułu";
                        ViewBag.Publisher = firstBook.TryGetProperty("publisher", out var p) ? p.GetString() : "Nieznane wydawnictwo";
                        ViewBag.PublishedDate = firstBook.TryGetProperty("publishedDate", out var pd) ? pd.GetString() : "Brak daty";
                        ViewBag.Description = firstBook.TryGetProperty("description", out var d) ? d.GetString() : "Brak opisu";
                        
                        var authorsList = new List<string>();
                        if (firstBook.TryGetProperty("authors", out var authorsArray))
                        {
                            foreach (var author in authorsArray.EnumerateArray())
                            {
                                authorsList.Add(author.GetString());
                            }
                        }
                        ViewBag.Authors = string.Join(", ", authorsList);

                        string thumbnailUrl = "";
                        if (firstBook.TryGetProperty("imageLinks", out var imageLinks))
                        {
                            thumbnailUrl = imageLinks.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : "";
                            if (!string.IsNullOrEmpty(thumbnailUrl) && thumbnailUrl.StartsWith("http://"))
                            {
                                thumbnailUrl = thumbnailUrl.Replace("http://", "https://");
                            }
                        }
                        ViewBag.ThumbnailUrl = thumbnailUrl;
                    }
                    else
                    {
                        ViewBag.Message = "Nie znaleziono żadnej książki dla podanego zapytania.";
                    }
                }
                else
                {
                    // Pobieramy kod błędu, żeby wiedzieć co poszło nie tak (np. 403, 400)
                    ViewBag.Error = $"Błąd zewnętrznego API. Status: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Błąd połączenia z API: " + ex.Message;
            }

            return View("Index");
        }

        [HttpPost]
        public async Task<IActionResult> SaveToDatabase(string title, string authors, string publisher, string description, string imageUrl)
        {
            if (string.IsNullOrEmpty(title))
            {
                TempData["Error"] = "Nie udało się zapisać książki – brak tytułu.";
                return RedirectToAction("Index");
            }

            try
            {
                // Mapujemy parametry z API na polskie nazwy pól w Twoim modelu Book
                var newBook = new Book
                {
                    Tytul = title,
                    Autor = string.IsNullOrEmpty(authors) ? "Nieznany autor" : authors,
                    Wydawnictwo = string.IsNullOrEmpty(publisher) ? "Nieznane wydawnictwo" : publisher,
            
                    // Bezpieczne przycinanie opisu do polskiego pola 'Opis'
                    Opis = description?.Length > 1000 ? description.Substring(0, 997) + "..." : description,
            
                    ImageUrl = imageUrl,
                    Cena = 0.00m,                     // Używamy 'Cena' zamiast 'Price'
                    IloscEgzemplarzy = 1,             // Używamy 'IloscEgzemplarzy'
                    IloscDoSprzedazy = 0, 
                    JezykWydania = "polski",
                    NumerWydania = "I"
                };

                // Dodanie nowego rekordu do bazy danych za pomocą EF Core
                _context.Books.Add(newBook);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Książka \"{newBook.Tytul}\" została pomyślnie zapisana!";
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Error"] = "Błąd podczas zapisu do bazy danych: " + innerMessage;
            }

            return RedirectToAction("Index");
        }
    }
}