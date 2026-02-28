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
            .AddGrpcServices()
            .AddGrpcClients(configuration)
            .AddSwaggerDocs();

        return services;
    }

    public static WebApplication UseApplication(this WebApplication app)
    {
        app.UseSwaggerDocs();
        app.MapControllers();
        app.MapGrpcEndpoints();
        return app;
    }
}
