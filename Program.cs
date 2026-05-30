using Microsoft.EntityFrameworkCore;

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
// 2. REJESTRACJA USŁUG SYSTEMOWYCH
// ==========================================================
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    // Zabezpieczenie przed blokowaniem ciasteczek sesji w środowisku Docker (HTTP Localhost)
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// ==========================================================
// 3. INICJALIZACJA BAZY DANYCH I TWORZENIE KONTA ADMINISTRATORA
// ==========================================================
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<meow.Models.LibraryDbContext>();
    
    // Upewnij się, że baza jest stworzona i ma aktualne tabele
    if (context.Database.GetPendingMigrations().Any())
    {
        context.Database.Migrate();
    }
    else
    {
        context.Database.EnsureCreated();
    }
    Console.WriteLine("🐾 Baza danych gotowa.");

    // Automatycznie dodajemy poprawnego Admina, jeśli nie istnieje w tabeli
    if (!context.Users.Any(u => u.Rola == "Admin"))
    {
        // 1. Tworzymy fikcyjnego klienta dla potrzeb relacji NOT NULL
        var adminKlient = new meow.Models.Klient
        {
            Imie = "Systemowy",
            Nazwisko = "Administrator",
            Email = "admin@meow.pl",
            Telefon = "000000000"
        };
        context.Klienci.Add(adminKlient);
        context.SaveChanges(); 

        // 2. Tworzymy użytkownika i SZYFRUJEMY jego hasło przez BCrypt!
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
app.UseStaticFiles();
app.UseRouting();

app.UseSession();          // ładuje sesję (w tym koszyk i użytkownika)
app.UseAuthorization();    // standardowe uprawnienia .NET

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();