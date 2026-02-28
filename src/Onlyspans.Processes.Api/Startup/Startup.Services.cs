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
}
