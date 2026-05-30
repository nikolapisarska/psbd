using Microsoft.EntityFrameworkCore;

namespace meow.Models
{
    public class LibraryDbContext : DbContext
    {
        public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options)
        {
        }

        public DbSet<Book> Books { get; set; }
        public DbSet<Egzemplarz> Egzemplarze { get; set; }
        public DbSet<Klient> Klienci { get; set; }
        public DbSet<Wypozyczenie> Wypozyczenia { get; set; }
        public DbSet<Platnosc> Platnosci { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Zamowienie> Zamowienia { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Jawne określenie precyzji dla cen i kar finansowych
            modelBuilder.Entity<Book>()
                .Property(b => b.Cena)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Platnosc>()
                .Property(p => p.Kwota)
                .HasPrecision(18, 2);

         //zabezpieczenia relacji
            modelBuilder.Entity<Wypozyczenie>()
                .HasOne(w => w.Book)
                .WithMany()
                .HasForeignKey(w => w.IdKsiazki)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Klient)
                .WithMany()
                .HasForeignKey(u => u.KlientId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}