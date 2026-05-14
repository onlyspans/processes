namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:3000"];

        services
            .AddDatabase(configuration)
            .AddProcessServices(configuration)
            .AddCors(o => o.AddPolicy("open", p => p
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()))
            .AddGrpcServices()
            .AddGrpcClients(configuration)
            .AddSwaggerDocs();

        return services;
    }

    public static WebApplication UseApplication(this WebApplication app)
    {
        app.UseSwaggerDocs();
        app.UseCors("open");
        app.UseRequestTimeouts();
        app.MapControllers();
        app.MapHub<Features.Deployment.DeploymentLogsHub>("/hubs/deployment-logs")
            .RequireCors("open");
        app.MapGrpcEndpoints();
        return app;
    }
}
