using Microsoft.EntityFrameworkCore;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// ==========================================================
// 1. POŁĄCZENIE Z BAZĄ DANYCH MYSQL
// ==========================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "server=meow_db;port=3306;database=wypozyczalnia_ksiazek;user=root;password=rootpassword;";
builder.Services.AddDbContext<meow.Models.LibraryDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 30)), 
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

// ==========================================================
// 2. REJESTRACJA USŁUG SYSTEMOWYCH I WŁASNYCH
// ==========================================================
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "pl", "en" };
    options.SetDefaultCulture(supportedCultures[0])
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
    
    options.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider());
});

builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddTransient<IEmailService, SmtpEmailService>();

var app = builder.Build();

// ==========================================================
// 3. INICJALIZACJA BAZY DANYCH I TWORZENIE KONTA ADMINISTRATORA
// ==========================================================
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<meow.Models.LibraryDbContext>();
    if (context.Database.GetPendingMigrations().Any())
    {
        context.Database.Migrate();
    }
    else
    {
        context.Database.EnsureCreated();
    }
    Console.WriteLine("🐾 Baza danych gotowa.");

    if (!context.Users.Any(u => u.Rola == "Admin"))
    {
        var adminKlient = new meow.Models.Klient
        {
            Imie = "Systemowy",
            Nazwisko = "Administrator",
            Email = "admin@meow.pl",
            Telefon = "000000000"
        };
        context.Klienci.Add(adminKlient);
        context.SaveChanges(); 

        var systemAdmin = new meow.Models.User
        {
            Login = "superadmin",
            Haslo = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Rola = "Admin",
            KlientId = adminKlient.IdKlienta 
        };
        context.Users.Add(systemAdmin);
        context.SaveChanges();
        Console.WriteLine("👑 Konto administratora (superadmin / admin123) zostało pomyślnie utworzone w BCrypt!");
    }
}

// ==========================================================
// 4. KONFIGURACJA POTOKU ŻĄDAŃ HTTP (MIDDLEWARE PIPELINE)
// ==========================================================
var localizationOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseRouting();
app.UseSession();          
app.UseAuthorization();    

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

//==========================================================
// 5. DEFINICJE TYPÓW (INTERFEJSY I KLASY)
// ==========================================================
public interface IEmailService
{
    
    Task SendWelcomeEmailAsync(string toEmail, string userName);
    Task SendOrderEmailAsync(string toEmail, string userName, string orderNumber, string bodyText);
    Task SendStatusUpdateEmailAsync(string toEmail, string userName, string orderNumber, string newStatus);
    Task SendReservationEmailAsync(string toEmail, string userName, string bookTitle, string inventoryNumber, DateTime deadline); 
    Task SendLoanConfirmationEmailAsync(string toEmail, string userName, string bookTitle, DateTime dueDate);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    public SmtpEmailService(IConfiguration configuration) { _configuration = configuration; }

    public async Task SendWelcomeEmailAsync(string toEmail, string userName)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Biblioteka Meow 🐾", "ksiegarniameow@gmail.com"));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Witaj w gronie czytelników E-księgarni Meow!";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $"<h1>Witaj {userName}!</h1><p>Twoje konto (login: {userName}) zostało pomyślnie utworzone. Życzymy miłego czytania i/lub zakupów! 🐱📖</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

        using (var client = new SmtpClient())
        {
            try
            {
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ksiegarniameow@gmail.com", "kqxcgikrfdmpzrkv");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                Console.WriteLine($"🚀 Mail powitalny wysłany przez MailKit do: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Błąd MailKit podczas wysyłania maila: {ex.Message}");
            }
        }
    }

    public async Task SendOrderEmailAsync(string toEmail, string userName, string orderNumber, string bodyText)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Biblioteka Meow 🐾", "ksiegarniameow@gmail.com"));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = $"Potwierdzenie zamówienia {orderNumber} 🐾";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $"<h2>Cześć {userName}!</h2><p>{bodyText.Replace("\n", "<br/>")}</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

        using (var client = new SmtpClient())
        {
            try
            {
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ksiegarniameow@gmail.com", "kqxcgikrfdmpzrkv");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                Console.WriteLine($"🚀 Mail z potwierdzeniem zamówienia {orderNumber} wysłany przez MailKit do: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Błąd MailKit podczas wysyłania maila zamówienia: {ex.Message}");
            }
        }
    }

    public async Task SendStatusUpdateEmailAsync(string toEmail, string userName, string orderNumber, string newStatus)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Biblioteka Meow 🐾", "ksiegarniameow@gmail.com"));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = $"Zmiana statusu zamówienia {orderNumber} 🐾";

        string statusText = newStatus == "Wysłane" || newStatus == "Nadane" 
            ? $"Twoja paczka o numerze śledzenia <strong>{orderNumber}</strong> została właśnie nadana i ruszyła w drogę! 🚀" 
            : $"Status Twojego zamówienia o numerze śledzenia <strong>{orderNumber}</strong> zmienił się na: <strong>{newStatus}</strong>.";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
                <h2>Cześć {userName}!</h2>
                <p>Mamy dla Ciebie nowe informacje dotyczące Twoich zakupów w e-księgarni meow.</p>
                <p>{statusText}</p>
                <br/>
                <p>Mruczącego dnia,<br/>Zespół meow 🐾</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

        using (var client = new SmtpClient())
        {
            try
            {
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ksiegarniameow@gmail.com", "kqxcgikrfdmpzrkv");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                Console.WriteLine($"🚀 Mail ze zmianą statusu dla {orderNumber} wysłany przez MailKit do: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Błąd MailKit podczas wysyłania maila o statusie: {ex.Message}");
            }
        }
    }

    // IMPLEMENTACJA NOWEJ METODY POWIADOMIENIA O REZERWACJI STACJONARNEJ
    public async Task SendReservationEmailAsync(string toEmail, string userName, string bookTitle, string inventoryNumber, DateTime deadline)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Biblioteka Meow 🐾", "ksiegarniameow@gmail.com"));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Potwierdzenie rezerwacji książki stacjonarnej 🐾";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
                <h2>Cześć {userName}!</h2>
                <p>Pomyślnie zarezerwowałeś online egzemplarz książki w naszej bibliotece stacjonarnej.</p>
                <p><strong>Szczegóły rezerwacji:</strong></p>
                <ul>
                    <li><strong>Tytuł:</strong> „{bookTitle}”</li>
                    <li><strong>Numer inwentarzowy:</strong> #{inventoryNumber}</li>
                </ul>
                <p>🐾 Masz <strong>3 dni na odbiór</strong> książki w naszej placówce stacjonarnej.</p>
                <p>Czekamy na Ciebie do dnia: <strong>{deadline:dd.MM.yyyy} r. do godziny 18:00</strong>.</p>
                <br/>
                <p>Mruczącego dnia,<br/>Zespół meow 🐾</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

        using (var client = new SmtpClient())
        {
            try
            {
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ksiegarniameow@gmail.com", "kqxcgikrfdmpzrkv");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                Console.WriteLine($"🚀 Mail z potwierdzeniem rezerwacji wysłany przez MailKit do: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Błąd MailKit podczas wysyłania maila rezerwacji: {ex.Message}");
            }
        }
    }
    public async Task SendLoanConfirmationEmailAsync(string toEmail, string userName, string bookTitle, DateTime dueDate)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Biblioteka Meow 🐾", "ksiegarniameow@gmail.com"));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Potwierdzenie odebrania książki z biblioteki 🐾";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
            <h2>Cześć {userName}!</h2>
            <p>Książka została pomyślnie wydana w naszej placówce stacjonarnej.</p>
            <p><strong>Wypożyczona pozycja:</strong> „{bookTitle}”</p>
            <p>🐾 Czas na czytanie to standardowe 30 dni. Regulaminowy termin zwrotu upływa dnia:</p>
            <h3><strong>{dueDate:dd.MM.yyyy} r.</strong></h3>
            <p>Pamiętaj o terminowym zwrocie, aby uniknąć naliczania opłat karnych (0,50 zł za każdy dzień zwłoki).</p>
            <br/>
            <p>Życzymy mruczącej i udanej lektury!,<br/>Zespół meow 🐾</p>"
        };
        message.Body = bodyBuilder.ToMessageBody();

        using (var client = new MailKit.Net.Smtp.SmtpClient())
        {
            try
            {
                await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("ksiegarniameow@gmail.com", "kqxcgikrfdmpzrkv");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                Console.WriteLine($"🚀 Mail z potwierdzeniem wydania książki wysłany do: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Błąd MailKit podczas wysyłania maila wydania: {ex.Message}");
            }
        }
    }
}