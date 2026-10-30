namespace WorkflowAutomation.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkflowAutomation.Identity.Core.Entities;
using WorkflowAutomation.Infrastructure.Persistence;
using WorkflowAutomation.Integration.Tests.Fixtures;
using WorkflowAutomation.Shared.Kernel.Context;
using WorkflowAutomation.Shared.Kernel.Time;
using WorkflowAutomation.Workflows.Core;
using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using WorkflowAutomation.Workspaces.Core.Entities;
using Xunit;

public class WorkflowPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;
    private readonly SystemClock _clock = new();
    private readonly KahnDagValidator _validator = new();

    public WorkflowPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenValidWorkflowAndVersion_WhenSaved_CanBeRetrievedInSeparateScope()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // 1. Arrange & Act: Seed and persist in Scope 1
        using (var scope = _fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Satisfy prerequisites: AspNetUsers & Workspaces
            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            db.Users.Add(user);
            db.Workspaces.Add(workspace);

            // Create Workflow and draft WorkflowVersion
            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Customer Onboarding", "Onboarding flow", _clock);
            var definition = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "SendEmail" }]
            };
            var version = WorkflowVersion.CreateDraft(versionId, workspaceId, workflowId, 1, definition, _validator, _clock);

            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(version);

            await db.SaveChangesAsync();
        }

        // 2. Assert: Query back from a fresh, isolated Scope 2
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            verifyContext.SetContext(workspaceId, userId);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var persistedWorkflow = await verifyDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(persistedWorkflow);
            Assert.Equal("Customer Onboarding", persistedWorkflow.Name);

            var persistedVersion = await verifyDb.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(persistedVersion);
            Assert.Equal(1, persistedVersion.VersionNumber);
            Assert.False(persistedVersion.IsPublished);

            // Verify jsonb round-trip and deserialization
            var roundTripDefinition = persistedVersion.GetDefinition();
            Assert.NotNull(roundTripDefinition);
            Assert.Single(roundTripDefinition.Steps);
            Assert.Equal("step-1", roundTripDefinition.Steps[0].Id);
            Assert.Equal("SendEmail", roundTripDefinition.Steps[0].Type);
        }
    }
    [Fact]
    public async Task GivenPublishedVersion_WhenActivated_WorkflowAndActiveVersionArePersisted()
    {
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // 1. Arrange & Act: Create, publish, activate, and persist in Scope 1
        using (var scope = _fixture.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            context.SetContext(workspaceId, userId);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new ApplicationUser { Id = userId, UserName = $"user_{userId:N}", Email = $"{userId:N}@test.com" };
            var workspace = Workspace.CreatePersonal(workspaceId, userId, "Test Workspace");
            db.Users.Add(user);
            db.Workspaces.Add(workspace);

            var workflow = Workflow.Create(workflowId, workspaceId, userId, "Invoice Processing", "Processes invoices", _clock);
            var definition = new WorkflowDefinition
            {
                Steps = [new StepDefinition { Id = "step-1", Type = "ParseInvoice" }]
            };
            var version = WorkflowVersion.CreateDraft(versionId, workspaceId, workflowId, 1, definition, _validator, _clock);

            // Domain lifecycle: publish version, persist the version, then activate the workflow
            version.Publish(_clock);

            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(version);

            await db.SaveChangesAsync();

            workflow.ActivateVersion(version, _clock);
            workflow.Activate(_clock);

            await db.SaveChangesAsync();
        }

        // 2. Assert: Verify in a fresh Scope 2
        using (var verifyScope = _fixture.CreateScope())
        {
            var verifyContext = verifyScope.ServiceProvider.GetRequiredService<WorkspaceContext>();
            verifyContext.SetContext(workspaceId, userId);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var persistedWorkflow = await verifyDb.Workflows.FindAsync(workflowId);
            Assert.NotNull(persistedWorkflow);
            Assert.True(persistedWorkflow.IsActive);
            Assert.Equal(versionId, persistedWorkflow.ActiveVersionId);

            var persistedVersion = await verifyDb.WorkflowVersions.FindAsync(versionId);
            Assert.NotNull(persistedVersion);
            Assert.True(persistedVersion.IsPublished);
            Assert.NotNull(persistedVersion.PublishedAt);
        }
    }

}
