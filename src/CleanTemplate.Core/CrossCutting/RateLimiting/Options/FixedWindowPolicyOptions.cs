namespace CleanTemplate.Core.CrossCutting.RateLimiting.Options;

public sealed class FixedWindowPolicyOptions
{
    public int PermitLimit { get; init; } = 120;
    public int WindowSeconds { get; init; } = 60;
    public int QueueLimit { get; init; }
}
