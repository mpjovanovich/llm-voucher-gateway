using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Persistence;

internal static class PersistenceRegistration
{
    /// <summary>
    /// Registers the voucher database context against the configured SQLite file.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddDbContext<VoucherDbContext>((serviceProvider, dbContextOptions) =>
        {
            DatabaseOptions databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>().Value;

            // Built rather than concatenated so a path containing a space or a semicolon
            // cannot alter the rest of the connection string.
            SqliteConnectionStringBuilder connectionString = new()
            {
                DataSource = databaseOptions.Path,
            };

            dbContextOptions.UseSqlite(connectionString.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Brings the voucher database up to the latest migration before the app serves traffic.
    /// </summary>
    /// <remarks>
    /// Applied on boot so the container is self-sufficient. This is right for one
    /// instance and wrong for two, because both would race to apply the same migration.
    /// </remarks>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();

        DatabaseOptions databaseOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<DatabaseOptions>>().Value;

        // SQLite creates the database file on demand but not the directory holding it.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databaseOptions.Path))!);

        await scope.ServiceProvider.GetRequiredService<VoucherDbContext>().Database.MigrateAsync();
    }
}
