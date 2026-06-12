using Microsoft.EntityFrameworkCore;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

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

// [PUNKT 16 - KONSUMPCJA API] Rejestracja fabryki klientów HTTP (Wykład 5: Protokół HTTP)
builder.Services.AddHttpClient();

// [PUNKT 12 - LOKALIZACJA] Konfiguracja lokalizacji i ścieżki do zasobów (Wykład 13-15)
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { "pl", "en" };
    options.SetDefaultCulture(supportedCultures[0]) // Domyślnie polski
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
    
    // Pozwalamy na przekazywanie języka w query stringu (?culture=en) lub ciasteczku
    options.RequestCultureProviders.Insert(0, new Microsoft.AspNetCore.Localization.QueryStringRequestCultureProvider());
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
// [PUNKT 12 - LOKALIZACJA] Uruchomienie middleware lokalizacji PRZED routingiem i statycznymi plikami
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
    Task SendStatusUpdateEmailAsync(string toEmail, string userName, string orderNumber, string newStatus); // <-- NOWA METODA
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

        using (var client = new MailKit.Net.Smtp.SmtpClient())
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

        using (var client = new MailKit.Net.Smtp.SmtpClient())
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

    // IMPLEMENTACJA NOWEJ METODY POWIADOMIENIA O ZMIANIE STATUSU
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

        using (var client = new MailKit.Net.Smtp.SmtpClient())
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
}