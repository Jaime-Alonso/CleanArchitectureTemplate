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
    public void Constructor_AssignsVersion7Identifier()
    {
        var product = new Product("Keyboard", "Mechanical", 0m, 10);

        Assert.Equal(7, product.Id.Version);
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

    [Fact]
    public void Limits_AreEnforcedBeforeUpdateMutatesAnything()
    {
        var product = new Product("name", new string('x', Product.DescriptionMaxLength), Product.PriceMaxValue, 0);
        foreach (var (description, price, stock) in new[]
                 {
                     (new string('x', Product.DescriptionMaxLength + 1), 1m, 1),
                     ("ok", Product.PriceMaxValue + 0.01m, 1),
                     ("ok", 1.001m, 1),
                     ("ok", -0.01m, 1),
                     ("ok", 1m, -1)
                 })
        {
            Assert.Throws<DomainValidationException>(() => product.Update("changed", description, price, stock));
            Assert.Equal("name", product.Name);
            Assert.Equal(Product.DescriptionMaxLength, product.Description!.Length);
            Assert.Equal(Product.PriceMaxValue, product.Price);
            Assert.Equal(0, product.Stock);
            Assert.Equal(1, product.Version);
            Assert.Throws<DomainValidationException>(() => new Product("name", description, price, stock));
        }
    }

    [Fact]
    public void Utf16AndTrailingZerosUseNumericAndDotNetLengthSemantics()
    {
        var name = string.Concat(Enumerable.Repeat("😀", 100));
        var description = string.Concat(Enumerable.Repeat("😀", 500));
        var product = new Product(name, description, 1.000m, 1);
        Assert.Equal(1m, product.Price);
        Assert.Throws<DomainValidationException>(() => new Product(name + "x", description, 1m, 1));
        Assert.Throws<DomainValidationException>(() => product.Update("changed", description + "x", 1m, 1));
        Assert.Equal(name, product.Name);
        Assert.Equal(description, product.Description);
        Assert.Equal(1, product.Version);
    }
}
