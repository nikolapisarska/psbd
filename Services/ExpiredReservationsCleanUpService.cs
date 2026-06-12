using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using meow.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace meow.Services
{
    public class ExpiredReservationsCleanUpService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Jak często sprawdzać bazę

        public ExpiredReservationsCleanUpService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanUpExpiredReservationsAsync();
                }
                catch (Exception ex)
                {
                    // Tutaj można dodać logowanie błędów, aby aplikacja nie scrashowała
                    Console.WriteLine($"❌ Błąd podczas czyszczenia rezerwacji: {ex.Message}");
                }

                // Oczekiwanie na kolejny cykl
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CleanUpExpiredReservationsAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

                var teraz = DateTime.Now;

                // Wyszukujemy rezerwacje online, których termin odbioru (DataPlanowanegoZwrotu) minął,
                // a książka nie została zwrócona ani odebrana (weryfikacja po IdKsiazki dodawanym przy rezerwacji)
                var przeterminowaneRezerwacje = await context.Wypozyczenia
                    .Where(w => w.DataZwrotu == null 
                             && w.IdKsiazki != null 
                             && w.DataPlanowanegoZwrotu < teraz)
                    // Dodatkowy warunek upewniający się, że to rezerwacja 3-dniowa, a nie trwające 30-dniowe wypożyczenie
                    .Where(w => w.DataPlanowanegoZwrotu <= w.DataWypozyczenia.AddDays(4)) 
                    .ToListAsync();

                if (przeterminowaneRezerwacje.Any())
                {
                    Console.WriteLine($"🐾 Znaleziono {przeterminowaneRezerwacje.Count} przeterminowanych rezerwacji. Rozpoczynam usuwanie...");
                    
                    // Możesz je usunąć fizycznie z bazy:
                    context.Wypozyczenia.RemoveRange(przeterminowaneRezerwacje);
                    
                    /* Albo opcjonalnie zamiast usuwać, możesz ustawić np. jakąś flagę lub specyficzną datę zwrotu, 
                    jeśli chcesz trzymać historię nieodebranych rezerwacji, np:
                    foreach(var rez in przeterminowaneRezerwacje) {
                        rez.DataZwrotu = teraz; // jako znacznik zamknięcia
                    }
                    */

                    await context.SaveChangesAsync();
                    Console.WriteLine("✔ Przeterminowane rezerwacje zostały pomyślnie anulowane, a egzemplarze odblokowane.");
                }
            }
        }
    }
}