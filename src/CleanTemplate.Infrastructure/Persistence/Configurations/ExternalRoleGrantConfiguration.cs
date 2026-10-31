using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanTemplate.Infrastructure.Persistence.Configurations;

public sealed class ExternalRoleGrantConfiguration : IEntityTypeConfiguration<ExternalRoleGrant>
{
    public void Configure(EntityTypeBuilder<ExternalRoleGrant> builder)
    {
        builder.ToTable("ExternalRoleGrants");
        builder.HasKey(grant => new { grant.UserId, grant.LoginProvider, grant.ProviderKey, grant.RoleId });
        builder.Property(grant => grant.LoginProvider).HasMaxLength(128);
        builder.Property(grant => grant.ProviderKey).HasMaxLength(128);
        builder.HasIndex(grant => new { grant.UserId, grant.ValidUntilUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(grant => grant.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
