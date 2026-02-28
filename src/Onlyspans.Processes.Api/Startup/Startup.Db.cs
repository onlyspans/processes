using Microsoft.EntityFrameworkCore;
using Onlyspans.Processes.Api.Data.Contexts;

namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Processes")
            ?? throw new InvalidOperationException(
                "Connection string 'Processes' is not configured");

        services.AddDbContext<ProcessesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations", "public");
                npgsql.CommandTimeout(30);
            }));

        return services;
    }

    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessesDbContext>();
        await db.Database.MigrateAsync();
    }
}
