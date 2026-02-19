using System;
using Microsoft.AspNetCore.Identity;

namespace CleanTemplate.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public string? TenantId { get; set; }
}
