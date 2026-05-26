using System.Linq;
using CleanTemplate.Domain.Entities;
using CleanTemplate.SharedKernel.Results;

namespace CleanTemplate.Domain.Tests.Architecture;

public sealed class DomainArchitectureTests
{
    [Fact]
    public void DomainAssembly_DoesNotReference_AspNetCoreOrOpenTelemetry()
    {
        var references = typeof(Product).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(references, name => name?.StartsWith("Microsoft.AspNetCore") == true);
        Assert.DoesNotContain(references, name => name?.StartsWith("OpenTelemetry") == true);
    }

    [Fact]
    public void SharedKernelAssembly_DoesNotReference_AspNetCoreOrOpenTelemetry()
    {
        var references = typeof(Result).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(references, name => name?.StartsWith("Microsoft.AspNetCore") == true);
        Assert.DoesNotContain(references, name => name?.StartsWith("OpenTelemetry") == true);
    }
}
