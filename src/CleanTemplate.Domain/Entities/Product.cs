using CleanTemplate.Domain.Exceptions;
using CleanTemplate.SharedKernel.Entities;

namespace CleanTemplate.Domain.Entities;

public sealed class Product : Entity
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
    public const decimal PriceMaxValue = 9999999999999999.99m;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int Stock { get; private set; }
    public long Version { get; private set; } = 1;

    private Product() { }

    public Product(string name, string? description, decimal price, int stock)
    {
        ValidateName(name);
        ValidateDescription(description);
        ValidatePrice(price);
        ValidateStock(stock);

        Name = name;
        Description = description;
        Price = price;
        Stock = stock;
    }

    public void Update(string name, string? description, decimal price, int stock)
    {
        ValidateName(name);
        ValidateDescription(description);
        ValidatePrice(price);
        ValidateStock(stock);

        var nextVersion = checked(Version + 1);

        Name = name;
        Description = description;
        Price = price;
        Stock = stock;
        Version = nextVersion;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Products.Name.Empty", "Name cannot be empty");

        if (name.Length > NameMaxLength)
            throw new DomainValidationException("Products.Name.TooLong", $"Name cannot exceed {NameMaxLength} characters");
    }

    private static void ValidatePrice(decimal price)
    {
        if (price < 0)
            throw new DomainValidationException("Products.Price.Negative", "Price cannot be negative");
        if (!IsValidPrice(price))
            throw new DomainValidationException("Products.Price.Invalid", "Price exceeds the supported range or scale");
    }

    public static bool IsValidPrice(decimal price) =>
        price >= 0 && price <= PriceMaxValue && decimal.Round(price, PriceScale) == price;

    private static void ValidateDescription(string? description)
    {
        if (description?.Length > DescriptionMaxLength)
            throw new DomainValidationException("Products.Description.TooLong", $"Description cannot exceed {DescriptionMaxLength} characters");
    }

    private static void ValidateStock(int stock)
    {
        if (stock < 0)
            throw new DomainValidationException("Products.Stock.Negative", "Stock cannot be negative");
    }
}
