using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace Onlyspans.Processes.Api.Tests.Fixtures;

public sealed class DbFixture : IAsyncLifetime
{
    public PostgreSqlContainer Db { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:15.5-alpine3.19")
        .WithDatabase("processes_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithPortBinding(5432, true)
        .WithWaitStrategy(
            Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    public string ConnectionString => Db.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Db.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.StopAsync(TestContext.Current.CancellationToken);
        await Db.DisposeAsync();
    }
}
