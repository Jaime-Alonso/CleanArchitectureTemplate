using System;
using CleanTemplate.Application.Abstractions;
using CleanTemplate.Infrastructure.Identity;
using CleanTemplate.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CleanTemplate.Infrastructure.Persistence;

public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    IQueryable<TEntity> IApplicationDbContext.Set<TEntity>() => Set<TEntity>().AsQueryable();

    void IApplicationDbContext.Add<TEntity>(TEntity entity) => base.Add(entity);

    void IApplicationDbContext.Remove<TEntity>(TEntity entity) => base.Remove(entity);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
