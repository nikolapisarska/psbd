using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace meow.Controllers
{
    public class CurrencyController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public CurrencyController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // Pobieranie danych z zewnętrznego API NBP (Wykład 5 - Protokół HTTP)
        public async Task<IActionResult> Index()
        {
            var client = _httpClientFactory.CreateClient();
            
            // Nagłówek HTTP informujący zewnętrzny serwer, jakiego formatu oczekujemy
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            try
            {
                var response = await client.GetAsync("https://api.nbp.pl/api/exchangerates/rates/a/eur/");
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    
                    var rate = doc.RootElement.GetProperty("rates")[0].GetProperty("mid").GetDouble();
                    ViewBag.EurRate = rate;
                }
                else
                {
                    ViewBag.EurRate = "Błąd odpowiedzi zewnętrznego serwera HTTP.";
                }
            }
            catch (Exception ex)
            {
                ViewBag.EurRate = "Błąd połączenia: " + ex.Message;
            }

            return View();
        }
    }
}