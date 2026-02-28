using Onlyspans.Processes.Api.Grpc.Services;

namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddGrpcServices(this IServiceCollection services)
    {
        services.AddGrpc();
        return services;
    }

    public static WebApplication MapGrpcEndpoints(this WebApplication app)
    {
        app.MapGrpcService<ProcessGrpcService>();
        return app;
    }
}
