using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevOps.GitHub.Sync.Data;

/// <summary>
/// Extension methods for registering Data-layer services in the dependency-injection container.
/// </summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/> configured for Azure SQL Server using
    /// the supplied connection string.
    /// </summary>
    public static IServiceCollection AddDataServices(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }
}
