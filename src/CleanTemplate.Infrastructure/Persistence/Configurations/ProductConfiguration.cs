using CleanTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanTemplate.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_Price_NonNegative", FormattableString.Invariant($"\"Price\" >= 0 AND \"Price\" <= {Product.PriceMaxValue}"));
            table.HasCheckConstraint("CK_Products_Stock_NonNegative", "\"Stock\" >= 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Product.NameMaxLength);

        builder.Property(p => p.Description)
            .HasMaxLength(Product.DescriptionMaxLength);

        builder.Property(p => p.Price)
            .HasPrecision(Product.PricePrecision, Product.PriceScale)
            .IsRequired();

        builder.Property(p => p.Stock)
            .IsRequired();

        builder.Property(p => p.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(1L);
    }
}
