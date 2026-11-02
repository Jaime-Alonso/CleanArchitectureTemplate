using System.Xml.Linq;

namespace CleanTemplate.ArchitectureTests;

public sealed class DependencyMatrixTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string> AllowedProjectReferences
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (project, references) in DependencyRules.AllowedReferences)
            {
                foreach (var reference in references)
                {
                    data.Add(project, reference);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void AllowedReference_IsAccepted(string project, string dependency)
    {
        var document = XDocument.Parse($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="../{dependency}/{dependency}.csproj" /></ItemGroup>
            </Project>
            """);

        Assert.Empty(DependencyRules.FindViolations(project, document));
    }

    [Fact]
    public void ProductionProjects_RespectTheExplicitDependencyMatrix()
    {
        var projects = Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        var names = projects.Select(Path.GetFileNameWithoutExtension).Order().ToArray();
        Assert.Equal(DependencyRules.AllowedReferences.Keys.Order().ToArray(), names);

        var violations = projects.SelectMany(path => DependencyRules.FindViolations(
            Path.GetFileNameWithoutExtension(path), XDocument.Load(path)));

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("CleanTemplate.SharedKernel", "CleanTemplate.Domain")]
    [InlineData("CleanTemplate.Domain", "CleanTemplate.Application")]
    [InlineData("CleanTemplate.Application", "CleanTemplate.Infrastructure")]
    [InlineData("CleanTemplate.Infrastructure", "CleanTemplate.Api")]
    [InlineData("CleanTemplate.Api", "CleanTemplate.Infrastructure")]
    [InlineData("CleanTemplate.Host", "CleanTemplate.Domain")]
    [InlineData("CleanTemplate.Crosscutting", "CleanTemplate.Application")]
    [InlineData("CleanTemplate.Migrations.SqlServer", "CleanTemplate.Host")]
    [InlineData("CleanTemplate.Migrations.PostgreSql", "CleanTemplate.Host")]
    public void ForbiddenReference_IsDetected_EvenWhenConditional(string project, string dependency)
    {
        var document = XDocument.Parse($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(Configuration)' == 'Release'">
                <ProjectReference Include="..\{dependency}\{dependency}.csproj" />
              </ItemGroup>
            </Project>
            """);

        var violation = Assert.Single(DependencyRules.FindViolations(project, document));
        Assert.Contains($"{project} -> {dependency}", violation);
    }

    [Theory]
    [InlineData("CleanTemplate.SharedKernel", "Mediora")]
    [InlineData("CleanTemplate.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("CleanTemplate.Application", "Microsoft.EntityFrameworkCore")]
    [InlineData("CleanTemplate.Application", "Dapper")]
    [InlineData("CleanTemplate.Application", "Microsoft.AspNetCore.OpenApi")]
    public void TechnicalPackageInCore_IsDetected(string project, string package)
    {
        var document = XDocument.Parse($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="{package}" /></ItemGroup>
            </Project>
            """);

        Assert.Contains(DependencyRules.FindViolations(project, document), violation => violation.Contains(package));
    }

    [Theory]
    [InlineData("CleanTemplate.SharedKernel")]
    [InlineData("CleanTemplate.Domain")]
    [InlineData("CleanTemplate.Application")]
    public void HttpFrameworkInCore_IsDetected(string project)
    {
        var document = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
            </Project>
            """);

        Assert.Contains(DependencyRules.FindViolations(project, document), violation => violation.Contains("framework"));
    }

    [Fact]
    public void UnknownProductionProject_RequiresAnExplicitRule()
    {
        Assert.NotEmpty(DependencyRules.FindViolations("UnreviewedProject", XDocument.Parse("<Project />")));
    }

    [Theory]
    [InlineData("CleanTemplate.SharedKernel")]
    [InlineData("CleanTemplate.Domain")]
    [InlineData("CleanTemplate.Application")]
    public void WebSdkInCore_IsDetected(string project)
    {
        var document = XDocument.Parse("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

        Assert.Contains(DependencyRules.FindViolations(project, document), violation => violation.Contains("Sdk"));
    }

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("ImplicitUsings", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    public void SharedBuildConfiguration_PreservesEssentialConventions(string property, string expected)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.Equal(expected, Assert.Single(document.Descendants(property)).Value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CleanArchitectureTemplate.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Run architecture tests from a repository checkout containing the solution and src directory.");
    }
}
