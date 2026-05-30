using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace meow.Controllers
{
    public class AITestController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View(); 
        }

        [HttpPost("/AITest/Ask")] 
        public async Task<IActionResult> Ask(string prompt)
        {
            try
            {
                using var client = new HttpClient();
                
                // Konfiguracja zapytania do modelu Mistral
                var requestBody = new 
                { 
                    model = "mistral", 
                    prompt = prompt, 
                    stream = false,
                    system = "Jesteś profesjonalnym asystentem w księgarni internetowej. Odpowiadaj zawsze w języku polskim, bądź uprzejmy i pomocny."
                };
                
                // Adres do lokalnej instancji Ollama w kontenerze
                var url = "http://host.docker.internal:11434/api/generate";
                
                var response = await client.PostAsJsonAsync(url, requestBody);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<dynamic>();
                    ViewBag.AIResponse = result.GetProperty("response").GetString();
                    ViewBag.LastPrompt = prompt; // Trzymamy zapytanie w formularzu
                }
                else
                {
                    ViewBag.AIResponse = "Błąd API: " + response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                // Łapanie błędów połączenia z usługą
                ViewBag.AIResponse = "Błąd połączenia: " + ex.Message;
            }

            return View("Index");
        }
    }
}