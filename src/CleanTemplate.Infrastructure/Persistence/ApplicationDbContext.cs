using CleanTemplate.Domain.Entities;
using CleanTemplate.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Persistence;

public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Product>().ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Products_Name_Utf16Length", FormattableString.Invariant($"2 * char_length(\"Name\") - char_length(regexp_replace(\"Name\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= {Product.NameMaxLength}"));
                table.HasCheckConstraint("CK_Products_Description_Utf16Length", FormattableString.Invariant($"2 * char_length(\"Description\") - char_length(regexp_replace(\"Description\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= {Product.DescriptionMaxLength}"));
            });
        }
        else if (Database.IsSqlite())
        {
            modelBuilder.Entity<Product>().ToTable("Products", table =>
                table.HasCheckConstraint("CK_Products_Price_NonNegative", FormattableString.Invariant($"CAST(\"Price\" AS NUMERIC) >= 0 AND CAST(\"Price\" AS NUMERIC) <= {Product.PriceMaxValue}")));
        }
    }
}
