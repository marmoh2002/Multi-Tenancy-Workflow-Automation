namespace WorkflowAutomation.Workflows.Core.Tests;

using WorkflowAutomation.Workflows.Core.Definitions;
using WorkflowAutomation.Workflows.Core.Entities;
using Xunit;

public class WorkflowVersionTests
{
    private readonly TestClock _clock = new();
    private readonly KahnDagValidator _validator = new();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private static WorkflowDefinition CreateSampleDefinition(string stepId = "step-1")
    {
        return new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition
                {
                    Id = stepId,
                    Type = "Log"
                }
            ]
        };
    }

    [Fact]
    public void CreateDraft_with_valid_definition_creates_unpublished_draft()
    {
        var versionId = Guid.NewGuid();
        var definition = CreateSampleDefinition("start-task");

        var version = WorkflowVersion.CreateDraft(
            versionId,
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            definition,
            _validator,
            _clock);

        Assert.Equal(versionId, version.Id);
        Assert.Equal(WorkspaceId, version.WorkspaceId);
        Assert.Equal(WorkflowId, version.WorkflowId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(WorkflowVersion.CurrentDefinitionSchemaVersion, version.DefinitionSchemaVersion);
        Assert.False(version.IsPublished);
        Assert.Null(version.PublishedAt);
        Assert.Null(version.ValidationErrorsJson);
        Assert.Equal(new DateTimeOffset(_clock.UtcNow), version.CreatedAt);
        Assert.Null(version.UpdatedAt);

        // Verify round-trip deserialization via GetDefinition()
        var retrievedDefinition = version.GetDefinition();
        Assert.NotNull(retrievedDefinition);
        Assert.Single(retrievedDefinition.Steps);
        Assert.Equal("start-task", retrievedDefinition.Steps[0].Id);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "00000000-0000-0000-0000-000000000000")]
    public void CreateDraft_rejects_empty_ids(string idStr, string workspaceIdStr, string workflowIdStr)
    {
        var id = Guid.Parse(idStr);
        var workspaceId = Guid.Parse(workspaceIdStr);
        var workflowId = Guid.Parse(workflowIdStr);

        Assert.Throws<ArgumentException>(() =>
            WorkflowVersion.CreateDraft(
                id,
                workspaceId,
                workflowId,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                _clock));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CreateDraft_rejects_non_positive_version_number(int invalidVersionNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkflowVersion.CreateDraft(
                Guid.NewGuid(),
                WorkspaceId,
                WorkflowId,
                invalidVersionNumber,
                CreateSampleDefinition(),
                _validator,
                _clock));
    }

    [Fact]
    public void CreateDraft_rejects_null_definition()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowVersion.CreateDraft(
                Guid.NewGuid(),
                WorkspaceId,
                WorkflowId,
                versionNumber: 1,
                definition: null!,
                _validator,
                _clock));
    }

    [Fact]
    public void CreateDraft_rejects_null_validator()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowVersion.CreateDraft(
                Guid.NewGuid(),
                WorkspaceId,
                WorkflowId,
                versionNumber: 1,
                CreateSampleDefinition(),
                validator: null!,
                _clock));
    }

    [Fact]
    public void CreateDraft_rejects_null_clock()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkflowVersion.CreateDraft(
                Guid.NewGuid(),
                WorkspaceId,
                WorkflowId,
                versionNumber: 1,
                CreateSampleDefinition(),
                _validator,
                clock: null!));
    }
    [Fact]
    public void CreateDraft_with_cycle_records_validation_errors_in_json()
    {
        var cyclicDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition
                {
                    Id = "step-a",
                    Type = "Log",
                    DependsOn = ["step-b"]
                },
                new StepDefinition
                {
                    Id = "step-b",
                    Type = "Log",
                    DependsOn = ["step-a"]
                }
            ]
        };

        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            cyclicDefinition,
            _validator,
            _clock);

        Assert.False(version.IsPublished);
        Assert.NotNull(version.ValidationErrorsJson);
        Assert.Contains("Cycle detected", version.ValidationErrorsJson);
    }

    [Fact]
    public void CreateDraft_with_missing_dependency_records_validation_errors_in_json()
    {
        var invalidDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition
                {
                    Id = "step-a",
                    Type = "Log",
                    DependsOn = ["non-existent-step"]
                }
            ]
        };

        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            invalidDefinition,
            _validator,
            _clock);

        Assert.False(version.IsPublished);
        Assert.NotNull(version.ValidationErrorsJson);
        Assert.Contains($"Step {invalidDefinition.Steps[0].Id} depends on unknown step non-existent-step.", version.ValidationErrorsJson);
    }

}
