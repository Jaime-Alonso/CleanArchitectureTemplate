using CleanTemplate.Domain.Entities;
using CleanTemplate.Domain.Exceptions;

namespace CleanTemplate.Domain.Tests.Entities;

public sealed class ProductTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesProduct()
    {
        var product = new Product("Keyboard", "Mechanical", 0m, 10);

        Assert.Equal("Keyboard", product.Name);
        Assert.Equal("Mechanical", product.Description);
        Assert.Equal(0m, product.Price);
        Assert.Equal(10, product.Stock);
        Assert.NotEqual(Guid.Empty, product.Id);
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => new Product("", "desc", 10m, 1));
    }

    [Fact]
    public void Constructor_WithNameLongerThanMaxLength_ThrowsDomainValidationException()
    {
        var tooLongName = new string('N', Product.NameMaxLength + 1);

        Assert.Throws<DomainValidationException>(() => new Product(tooLongName, "desc", 10m, 1));
    }

    [Fact]
    public void Constructor_WithNegativePrice_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => new Product("Keyboard", "desc", -1m, 1));
    }

    [Fact]
    public void Update_WithNegativeStock_ThrowsDomainValidationException()
    {
        var product = new Product("Keyboard", "desc", 10m, 1);

        Assert.Throws<DomainValidationException>(() => product.Update("Keyboard", "desc", 10m, -1));
    }
}
