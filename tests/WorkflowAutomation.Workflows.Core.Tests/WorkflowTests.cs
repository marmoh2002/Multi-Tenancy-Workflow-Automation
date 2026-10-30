namespace WorkflowAutomation.Workflows.Core.Tests;

using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using Xunit;

public class WorkflowTests
{
    private readonly TestClock _clock = new();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();

    //helper method
    private WorkflowVersion CreateDraftVersion(Guid workflowId, Guid workspaceId, int versionNumber = 1)
    {
        var definition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition
                {
                    Id = "step-1",
                    Type = "Log"
                }
            ]
        };
        return WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            workspaceId,
            workflowId,
            versionNumber,
            definition,
            new KahnDagValidator(),
            _clock);
    }
    private WorkflowVersion CreatePublishedVersion(Guid workflowId, Guid workspaceId, int versionNumber = 1)
    {
        var version = CreateDraftVersion(workflowId, workspaceId, versionNumber);
        version.Publish(new KahnDagValidator(), _clock);
        return version;
    }


    [Fact]
    public void Successful_creation_starts_inactive_without_active_version()
    {
        var id = Guid.NewGuid();

        var workflow = Workflow.Create(
            id,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            "Processes incoming store orders",
            _clock);

        Assert.Equal(id, workflow.Id);
        Assert.Equal(WorkspaceId, workflow.WorkspaceId);
        Assert.Equal(CreatorId, workflow.CreatedByUserId);
        Assert.Equal("Order Processing", workflow.Name);
        Assert.Equal("Processes incoming store orders", workflow.Description);
        Assert.False(workflow.IsActive);
        Assert.Null(workflow.ActiveVersionId);
        var expectedCreatedAt = new DateTimeOffset(_clock.UtcNow);
        Assert.Equal(expectedCreatedAt, workflow.CreatedAt);
        Assert.Null(workflow.UpdatedAt);
    }

    [Fact]
    public void Create_trims_name_and_description()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "   Order Processing   ",
            "   Trimmed description   ",
            _clock);

        Assert.Equal("Order Processing", workflow.Name);
        Assert.Equal("Trimmed description", workflow.Description);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "00000000-0000-0000-0000-000000000000")]
    public void Create_rejects_empty_ids(string idStr, string workspaceIdStr, string creatorIdStr)
    {
        var id = Guid.Parse(idStr);
        var workspaceId = Guid.Parse(workspaceIdStr);
        var creatorId = Guid.Parse(creatorIdStr);

        Assert.Throws<ArgumentException>(() =>
            Workflow.Create(id, workspaceId, creatorId, "Valid Name", null, _clock));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_or_whitespace_name(string? invalidName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Workflow.Create(Guid.NewGuid(), WorkspaceId, CreatorId, invalidName!, null, _clock));
    }

    [Fact]
    public void Create_rejects_overlong_name()
    {
        var longName = new string('A', 201);

        Assert.Throws<ArgumentException>(() =>
            Workflow.Create(Guid.NewGuid(), WorkspaceId, CreatorId, longName, null, _clock));
    }

    [Fact]
    public void Create_rejects_overlong_description()
    {
        var longDescription = new string('D', 2001);

        Assert.Throws<ArgumentException>(() =>
            Workflow.Create(Guid.NewGuid(), WorkspaceId, CreatorId, "Valid Name", longDescription, _clock));
    }

    [Fact]
    public void Create_rejects_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Workflow.Create(
                Guid.NewGuid(),
                WorkspaceId,
                CreatorId,
                "Valid Name",
                null,
                null!));
    }
    [Fact]
    public void UpdateDetails_updates_name_and_description_and_sets_updated_at()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Initial Name",
            "Initial Description",
            _clock);

        _clock.Advance(TimeSpan.FromMinutes(5));

        workflow.UpdateDetails("   Updated Name   ", "   Updated Description   ", _clock);

        Assert.Equal("Updated Name", workflow.Name);
        Assert.Equal("Updated Description", workflow.Description);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow), workflow.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_with_same_values_is_noop_and_preserves_timestamp()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Name",
            "Description",
            _clock);

        _clock.Advance(TimeSpan.FromMinutes(5));

        // Call UpdateDetails with values that trim to the existing values
        workflow.UpdateDetails("   Name   ", "Description", _clock);

        Assert.Null(workflow.UpdatedAt);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_rejects_missing_or_whitespace_name(string? invalidName)
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Valid Name",
            null,
            _clock);

        Assert.ThrowsAny<ArgumentException>(() =>
            workflow.UpdateDetails(invalidName!, null, _clock));
    }

    [Fact]
    public void UpdateDetails_rejects_overlong_name()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Valid Name",
            null,
            _clock);

        var longName = new string('A', 201);

        Assert.Throws<ArgumentException>(() =>
            workflow.UpdateDetails(longName, null, _clock));
    }

    [Fact]
    public void UpdateDetails_rejects_overlong_description()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Valid Name",
            null,
            _clock);

        var longDescription = new string('D', 2001);

        Assert.Throws<ArgumentException>(() =>
            workflow.UpdateDetails("Valid Name", longDescription, _clock));
    }

    [Fact]
    public void UpdateDetails_rejects_null_clock()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Valid Name",
            null,
            _clock);

        Assert.Throws<ArgumentNullException>(() =>
            workflow.UpdateDetails("Valid Name", null, null!));
    }
    [Fact]
    public void Activate_without_selected_version_is_rejected()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        Assert.Null(workflow.ActiveVersionId);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workflow.Activate(_clock));

        Assert.Contains("without a selected active version", exception.Message);
        Assert.False(workflow.IsActive);
    }

    [Fact]
    public void Deactivate_when_already_inactive_is_noop_and_preserves_timestamp()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        _clock.Advance(TimeSpan.FromMinutes(10));

        workflow.Deactivate(_clock);

        Assert.False(workflow.IsActive);
        Assert.Null(workflow.UpdatedAt);
    }
    [Fact]
    public void Version_selection_and_activation_lifecycle_succeeds()
    {
        var workflowId = Guid.NewGuid();
        var workflow = Workflow.Create(
            workflowId,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var version = CreatePublishedVersion(workflowId, WorkspaceId);

        // 1. Select the version
        _clock.Advance(TimeSpan.FromMinutes(2));
        workflow.ActivateVersion(version, _clock);

        Assert.Equal(version.Id, workflow.ActiveVersionId);
        Assert.False(workflow.IsActive);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow), workflow.UpdatedAt);

        // 2. Activate the workflow
        _clock.Advance(TimeSpan.FromMinutes(3));
        workflow.Activate(_clock);

        Assert.True(workflow.IsActive);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow), workflow.UpdatedAt);

        // 3. Deactivate the workflow
        _clock.Advance(TimeSpan.FromMinutes(4));
        workflow.Deactivate(_clock);

        Assert.False(workflow.IsActive);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow), workflow.UpdatedAt);
    }
    [Fact]
    public void ActivateVersion_rejects_unpublished_version()
    {
        var workflowId = Guid.NewGuid();
        var workflow = Workflow.Create(
            workflowId,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var draftVersion = CreateDraftVersion(workflowId, WorkspaceId);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workflow.ActivateVersion(draftVersion, _clock));

        Assert.Contains("Cannot activate an unpublished version", exception.Message);
        Assert.Null(workflow.ActiveVersionId);
    }

    [Fact]
    public void ActivateVersion_rejects_version_from_different_workflow()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var otherWorkflowVersion = CreatePublishedVersion(Guid.NewGuid(), WorkspaceId);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workflow.ActivateVersion(otherWorkflowVersion, _clock));

        Assert.Contains("belongs to a different workflow", exception.Message);
        Assert.Null(workflow.ActiveVersionId);
    }

    [Fact]
    public void ActivateVersion_rejects_version_from_different_workspace()
    {
        var workflowId = Guid.NewGuid();
        var workflow = Workflow.Create(
            workflowId,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var otherWorkspaceVersion = CreatePublishedVersion(workflowId, Guid.NewGuid());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workflow.ActivateVersion(otherWorkspaceVersion, _clock));

        Assert.Contains("belongs to a different workspace", exception.Message);
        Assert.Null(workflow.ActiveVersionId);
    }

    [Fact]
    public void ActivateVersion_with_same_active_version_is_noop_and_preserves_timestamp()
    {
        var workflowId = Guid.NewGuid();
        var workflow = Workflow.Create(
            workflowId,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var version = CreatePublishedVersion(workflowId, WorkspaceId);

        _clock.Advance(TimeSpan.FromMinutes(2));
        workflow.ActivateVersion(version, _clock);
        var initialUpdatedAt = workflow.UpdatedAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        workflow.ActivateVersion(version, _clock);

        Assert.Equal(version.Id, workflow.ActiveVersionId);
        Assert.Equal(initialUpdatedAt, workflow.UpdatedAt);
    }

    [Fact]
    public void Activate_when_already_active_is_noop_and_preserves_timestamp()
    {
        var workflowId = Guid.NewGuid();
        var workflow = Workflow.Create(
            workflowId,
            WorkspaceId,
            CreatorId,
            "Order Processing",
            null,
            _clock);

        var version = CreatePublishedVersion(workflowId, WorkspaceId);

        _clock.Advance(TimeSpan.FromMinutes(2));
        workflow.ActivateVersion(version, _clock);

        _clock.Advance(TimeSpan.FromMinutes(3));
        workflow.Activate(_clock);
        var activeUpdatedAt = workflow.UpdatedAt;

        _clock.Advance(TimeSpan.FromMinutes(5));
        workflow.Activate(_clock);

        Assert.True(workflow.IsActive);
        Assert.Equal(activeUpdatedAt, workflow.UpdatedAt);
    }


}
