using CleanTemplate.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanTemplate.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenFamilyConfiguration : IEntityTypeConfiguration<RefreshTokenFamily>
{
    public void Configure(EntityTypeBuilder<RefreshTokenFamily> builder)
    {
        builder.ToTable("RefreshTokenFamilies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SecurityStamp).HasMaxLength(256);
        builder.Property(x => x.RevocationReason).HasMaxLength(64);
        builder.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
