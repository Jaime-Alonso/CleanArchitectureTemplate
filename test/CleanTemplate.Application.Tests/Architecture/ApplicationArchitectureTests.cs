using System.Linq;
using CleanTemplate.Application.Products.Commands.CreateProduct;

namespace CleanTemplate.Application.Tests.Architecture;

public sealed class ApplicationArchitectureTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotReference_ApiOrHostAssemblies()
    {
        var references = typeof(CreateProductCommand).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(references, name => name == "CleanTemplate.Api");
        Assert.DoesNotContain(references, name => name == "CleanTemplate.Host");
    }
}
