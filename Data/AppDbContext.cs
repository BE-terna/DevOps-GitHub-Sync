using DevOps.GitHub.Sync.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DevOps.GitHub.Sync.Data;

/// <summary>
/// EF Core 10.0.0 database context for the DevOps GitHub Sync application.
/// EF Core 10 ships alongside .NET 10. Configured for Azure SQL Server using
/// code-first with data annotations.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<GitHubInstallation> GitHubInstallations => Set<GitHubInstallation>();

    public DbSet<SyncRequest> SyncRequests => Set<SyncRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique index: only one record per GitHub installation id
        modelBuilder.Entity<GitHubInstallation>()
            .HasIndex(i => i.InstallationId)
            .IsUnique();

        // Index for quick look-up by account login
        modelBuilder.Entity<GitHubInstallation>()
            .HasIndex(i => i.AccountLogin);

        // Index for quick look-up by status
        modelBuilder.Entity<SyncRequest>()
            .HasIndex(r => r.Status);
    }
}
