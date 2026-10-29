namespace WorkflowAutomation.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Infrastructure.Interceptors;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Shared.Kernel.Context;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructurePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        // 1. Register WorkspaceContext as Scoped
        services.AddScoped<WorkspaceContext>();

        // 2. Ensure IWorkspaceContext resolves to the exact same WorkspaceContext instance within the scope
        services.AddScoped<IWorkspaceContext>(serviceProvider => serviceProvider.GetRequiredService<WorkspaceContext>());
        // GetRequiredService asks the service provider: Give me the WorkspaceContext service registered in the current scope.

        // 3. Register the write validation interceptor as Scoped
        services.AddScoped<TenantWriteValidationInterceptor>();

        // 4. Register ApplicationDbContext as Scoped and unpooled
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);

            // Give EF Core this scope's checker.
            options.AddInterceptors(
                serviceProvider.GetRequiredService<
                    TenantWriteValidationInterceptor>());
        }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Scoped);

        return services;
    }
}
