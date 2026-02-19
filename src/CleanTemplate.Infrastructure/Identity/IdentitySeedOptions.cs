namespace CleanTemplate.Infrastructure.Identity;

public sealed class IdentitySeedOptions
{
    public string AdminEmail { get; init; } = "admin@local.template";
    public string AdminPassword { get; init; } = "__SET_VIA_USER_SECRETS_ADMIN_PASSWORD__";
}
