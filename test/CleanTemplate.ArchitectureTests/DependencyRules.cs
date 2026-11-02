using System.Xml.Linq;

namespace CleanTemplate.ArchitectureTests;

internal static class DependencyRules
{
    internal static IReadOnlyDictionary<string, string[]> AllowedReferences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CleanTemplate.SharedKernel"] = [],
            ["CleanTemplate.Domain"] = ["CleanTemplate.SharedKernel"],
            ["CleanTemplate.Application"] = ["CleanTemplate.Domain", "CleanTemplate.SharedKernel"],
            ["CleanTemplate.Infrastructure"] = ["CleanTemplate.Application"],
            ["CleanTemplate.Api"] = ["CleanTemplate.Application", "CleanTemplate.SharedKernel"],
            ["CleanTemplate.Crosscutting"] = [],
            ["CleanTemplate.Migrations.SqlServer"] = ["CleanTemplate.Infrastructure"],
            ["CleanTemplate.Migrations.PostgreSql"] = ["CleanTemplate.Infrastructure"],
            ["CleanTemplate.Host"] =
            [
                "CleanTemplate.Api", "CleanTemplate.Infrastructure", "CleanTemplate.Crosscutting",
                "CleanTemplate.Migrations.SqlServer", "CleanTemplate.Migrations.PostgreSql"
            ]
        };

    internal static IReadOnlyList<string> FindViolations(string project, XDocument document)
    {
        var violations = new List<string>();
        if (!AllowedReferences.TryGetValue(project, out var allowed))
        {
            violations.Add($"{project}: production project has no dependency rule.");
            return violations;
        }

        foreach (var reference in document.Descendants("ProjectReference"))
        {
            var path = ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', '/');
            var dependency = Path.GetFileNameWithoutExtension(path);
            if (!allowed.Contains(dependency, StringComparer.Ordinal))
            {
                violations.Add($"{project} -> {dependency}: forbidden project reference.");
            }
        }

        var pureCore = project is "CleanTemplate.SharedKernel" or "CleanTemplate.Domain";
        var application = project == "CleanTemplate.Application";
        if (pureCore || application)
        {
            if ((string?)document.Root?.Attribute("Sdk") != "Microsoft.NET.Sdk")
            {
                violations.Add($"{project}: core projects must use Microsoft.NET.Sdk.");
            }

            foreach (var reference in document.Descendants("FrameworkReference"))
            {
                violations.Add($"{project}: forbidden framework reference {reference.Attribute("Include")}.");
            }

            string[] applicationPackages =
                ["Mediora", "FluentValidation", "FluentValidation.DependencyInjectionExtensions", "Microsoft.Extensions.Logging.Abstractions"];
            foreach (var reference in document.Descendants("PackageReference"))
            {
                var package = (string?)reference.Attribute("Include") ?? string.Empty;
                if (pureCore || !applicationPackages.Contains(package, StringComparer.Ordinal))
                {
                    violations.Add($"{project}: forbidden package {package}.");
                }
            }
        }

        return violations;
    }
}
