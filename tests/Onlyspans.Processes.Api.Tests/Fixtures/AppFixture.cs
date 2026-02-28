using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Onlyspans.Processes.Api.Data.Contexts;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Variables;
using Onlyspans.Processes.Api.Grpc.Services;
using Onlyspans.Processes.Api.Startup;

namespace Onlyspans.Processes.Api.Tests.Fixtures;

public sealed class AppFixture : IAsyncLifetime
{
    public delegate void ConfigureServices(WebApplicationBuilder builder);

    private readonly DbFixture _dbFixture = new();

    public string ConnectionString => _dbFixture.ConnectionString;

    public async Task<WebApplication> BuildApplicationAsync(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection([
            new("ConnectionStrings:Processes", _dbFixture.ConnectionString),
            new("GrpcServices:Variables", "http://localhost:19999"),
            new("GrpcServices:Worker", "http://localhost:19998"),
            new("GrpcServices:ArtifactStorage", "http://localhost:19997"),
            new("Deployment:LogPath", Path.Combine(Path.GetTempPath(), "processes-test-logs", Guid.NewGuid().ToString())),
        ]);

        preconfigure?.Invoke(builder);

        builder.Services.AddApplication(builder.Configuration);

        builder.Logging.ClearProviders();

        var mockVariableResolver = Substitute.For<IVariableResolver>();
        mockVariableResolver
            .ResolveAsync(
                Arg.Any<IReadOnlyList<VariableDefinition>>(),
                Arg.Any<VariableResolutionContext?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var variables = callInfo.ArgAt<IReadOnlyList<VariableDefinition>>(0);
                var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var unresolved = new List<string>();

                foreach (var v in variables)
                {
                    if (!string.IsNullOrWhiteSpace(v.Value))
                        resolved[v.Name] = v.Value;
                    else
                        unresolved.Add(v.Name);
                }

                return new VariableResolutionResult
                {
                    Resolved = resolved,
                    Unresolved = unresolved,
                };
            });

        builder.Services.AddSingleton(mockVariableResolver);

        postconfigure?.Invoke(builder);

        var app = builder.Build();

        app.UseApplication();

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ProcessesDbContext>();
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
            await context.Processes.ExecuteDeleteAsync();
        }

        return app;
    }

    public ValueTask InitializeAsync()
        => _dbFixture.InitializeAsync();

    public ValueTask DisposeAsync()
        => _dbFixture.DisposeAsync();
}
