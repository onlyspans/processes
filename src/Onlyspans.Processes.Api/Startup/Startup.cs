namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddDatabase(configuration)
            .AddProcessServices(configuration)
            .AddCors(o => o.AddPolicy("open", p => p
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod()))
            .AddGrpcServices()
            .AddGrpcClients(configuration)
            .AddSwaggerDocs();

        return services;
    }

    public static WebApplication UseApplication(this WebApplication app)
    {
        app.UseSwaggerDocs();
        app.UseCors("open");
        app.MapControllers();
        app.MapHub<Features.Deployment.DeploymentLogsHub>("/hubs/deployment-logs");
        app.MapGrpcEndpoints();
        return app;
    }
}
