namespace WorkflowAutomation.Integration.Tests;

using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Infrastructure;
using WorkflowAutomation.Shared.Kernel.Context;

public class DependencyInjectionLifetimeTests
{
    [Fact]
    public void AddInfrastructurePersistence_ResolvesSameWorkspaceContextInstanceWithinScope()
    {
        // Arrange: Configure DI container
        var services = new ServiceCollection();
        services.AddInfrastructurePersistence("Host=localhost;Database=multi_tenancy_db;");

        using var provider = services.BuildServiceProvider();

        // Act: Open a simulated request scope
        using var scope = provider.CreateScope();
        var concreteInstance = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
        var interfaceInstance = scope.ServiceProvider.GetRequiredService<IWorkspaceContext>();

        // Assert: Both must point to the EXACT same object reference in memory
        Assert.Same(concreteInstance, interfaceInstance);
    }

    [Fact]
    public void AddInfrastructurePersistence_CreatesDistinctInstancesAcrossDifferentScopes()
    {
        // Arrange: Configure DI container
        var services = new ServiceCollection();
        services.AddInfrastructurePersistence("Host=localhost;Database=multi_tenancy_db;");

        using var provider = services.BuildServiceProvider();

        // Act: Resolve from Scope 1 (e.g., Request 1)
        WorkspaceContext contextFromScope1;
        using (var scope1 = provider.CreateScope())
        {
            contextFromScope1 = scope1.ServiceProvider.GetRequiredService<WorkspaceContext>();
        }

        // Resolve from Scope 2 (e.g., Request 2)
        WorkspaceContext contextFromScope2;
        using (var scope2 = provider.CreateScope())
        {
            contextFromScope2 = scope2.ServiceProvider.GetRequiredService<WorkspaceContext>();
        }

        // Assert: Scopes must NOT share state across requests
        Assert.NotSame(contextFromScope1, contextFromScope2);
    }
}
