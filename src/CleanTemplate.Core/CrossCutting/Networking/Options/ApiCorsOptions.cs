namespace CleanTemplate.Core.CrossCutting.Networking.Options;

public sealed class ApiCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "ApiCors";

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}
