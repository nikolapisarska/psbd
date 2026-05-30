using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace meow.Controllers
{
    public class AIChatController : Controller
    {
        [HttpGet]
        public IActionResult Index() 
        {
            return View();
        }

        [HttpPost("AIChat/Ask")]
        public async Task<IActionResult> Ask(string prompt)
        {
            using var client = new HttpClient();
            
            // Konfiguracja modelu i promptu systemowego
            var requestBody = new 
            { 
                model = "mistral", 
                prompt = prompt, 
                stream = false,
                system = "Jesteś asystentem w księgarni. Odpowiadaj zawsze po polsku."
            };

            try 
            {
                // Komunikacja z lokalną instancją Ollama
                var response = await client.PostAsJsonAsync("http://host.docker.internal:11434/api/generate", requestBody);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<dynamic>();
                    ViewBag.AIResponse = result.GetProperty("response").ToString();
                }
                else
                {
                    ViewBag.AIResponse = "Błąd komunikacji z serwerem AI.";
                }
            }
            catch (Exception ex)
            {
                ViewBag.AIResponse = "Błąd połączenia: " + ex.Message;
            }

            ViewBag.LastPrompt = prompt; // Zachowanie stanu formularza
            return View("Index");
        }
    }
}