using System;
using System.Collections.Generic;

namespace CleanTemplate.CrossCutting.RateLimiting.Options;

public sealed class ApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowPolicyOptions Global { get; init; } = new();
    public IReadOnlyDictionary<string, FixedWindowPolicyOptions> Policies { get; init; } =
        new Dictionary<string, FixedWindowPolicyOptions>(StringComparer.OrdinalIgnoreCase);
}
