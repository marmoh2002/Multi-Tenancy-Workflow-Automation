namespace WorkflowAutomation.Integration.Tests.Fixtures;

using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using WorkflowAutomation.Infrastructure;
using WorkflowAutomation.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// Spawns a dedicated, disposable PostgreSQL Docker container for integration tests
/// and initializes the EF Core schema via EnsureCreatedAsync.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("test_workflow_db")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
        .Build();

    private IServiceProvider _rootProvider = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        // 1. Start the isolated PostgreSQL Docker container
        await _container.StartAsync();

        // 2. Build the DI container calling the real production registration
        var services = new ServiceCollection();
        services.AddInfrastructurePersistence(ConnectionString);

        _rootProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // 3. Create the schema in PostgreSQL using a temporary scope
        using var initScope = _rootProvider.CreateScope();
        var db = initScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Creates a new isolated DI scope with scope validation enabled.
    /// </summary>
    public IServiceScope CreateScope() => _rootProvider.CreateScope();

    public async Task DisposeAsync()
    {
        if (_rootProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await _container.DisposeAsync();
    }
}
