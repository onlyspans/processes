using ArtifactStorage.Communication;
using Onlyspans.Processes.Api.Grpc.Services;
using Variables.Communication;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddGrpcClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var variablesAddress = configuration["GrpcServices:Variables"]
            ?? throw new InvalidOperationException(
                "GrpcServices:Variables address is not configured");

        services.AddGrpcClient<VariablesService.VariablesServiceClient>(options =>
        {
            options.Address = new Uri(variablesAddress);
        });

        var workerAddress = configuration["GrpcServices:Worker"]
            ?? throw new InvalidOperationException(
                "GrpcServices:Worker address is not configured");

        services.AddGrpcClient<WorkerService.WorkerServiceClient>(options =>
        {
            options.Address = new Uri(workerAddress);
        });

        var artifactStorageAddress = configuration["GrpcServices:ArtifactStorage"]
            ?? throw new InvalidOperationException(
                "GrpcServices:ArtifactStorage address is not configured");

        services.AddGrpcClient<ArtifactStorageService.ArtifactStorageServiceClient>(options =>
        {
            options.Address = new Uri(artifactStorageAddress);
        });

        services.AddScoped<VariablesGrpcService>();
        services.AddScoped<WorkerGrpcService>();
        services.AddScoped<ArtifactStorageGrpcService>();

        return services;
    }
}
