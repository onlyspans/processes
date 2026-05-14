using System.Globalization;
using FluentValidation;
using Onlyspans.Processes.Api.Features;
using Onlyspans.Processes.Api.Features.Deployment;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;
using Onlyspans.Processes.Api.Features.Variables;

namespace Onlyspans.Processes.Api.Startup;

public static partial class Startup
{
    public static IServiceCollection AddProcessServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddRequestTimeouts(options =>
            options.AddPolicy(
                "DeploymentExecute",
                DeploymentExecuteRequestTimeout(configuration)));
        services.AddSignalR();

        services.AddSingleton<IProcessDefinitionParser, YamlProcessDefinitionParser>();
        services.AddSingleton<PipelineGraphValidator>();

        var logBasePath = configuration["Deployment:LogPath"] ?? "logs/deployments";
        services.AddSingleton<IDeploymentLogWriter>(new FileDeploymentLogWriter(logBasePath));

        services.AddScoped<IValidator<ProcessDefinition>, ProcessDefinitionStructureValidator>();
        services.AddScoped<IProcessValidator, CompositeProcessValidator>();
        services.AddScoped<IVariableResolver, ExternalVariableResolver>();
        services.AddScoped<ProcessService>();
        services.AddScoped<DeploymentService>();

        return services;
    }

    private static TimeSpan DeploymentExecuteRequestTimeout(IConfiguration configuration)
    {
        var raw = configuration["Deployment:ExecuteRequestTimeout"];
        if (string.IsNullOrWhiteSpace(raw))
            return TimeSpan.FromMinutes(5);

        return TimeSpan.Parse(raw, CultureInfo.InvariantCulture);
    }
}
