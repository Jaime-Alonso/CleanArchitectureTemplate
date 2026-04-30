using CleanTemplate.Core.SharedKernel.Entities;
using CleanTemplate.Domain.Exceptions;

namespace CleanTemplate.Domain.Entities;

public sealed class Product : Entity
{
    public const int NameMaxLength = 200;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int Stock { get; private set; }

    private Product() { }

    public Product(string name, string? description, decimal price, int stock)
    {
        ValidateName(name);
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
        ValidatePrice(price);
        ValidateStock(stock);

        Name = name;
        Description = description;
        Price = price;
        Stock = stock;
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
    }

    private static void ValidateStock(int stock)
    {
        if (stock < 0)
            throw new DomainValidationException("Products.Stock.Negative", "Stock cannot be negative");
    }
}
