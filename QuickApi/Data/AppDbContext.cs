using Microsoft.EntityFrameworkCore;
using QuickApi.Models;

namespace QuickApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();

            e.Property(x => x.Price).HasColumnType("numeric(18,2)");
            e.Property(x => x.Quantity).HasDefaultValue(0);


            e.ToTable(t => t.HasCheckConstraint("CK_Products_Quantity_NonNegative", "\"Quantity\" >= 0"));
            e.ToTable(t => t.HasCheckConstraint("CK_Products_Price_NonNegative", "\"Price\" >= 0"));


        });
     b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
        });
}
}
