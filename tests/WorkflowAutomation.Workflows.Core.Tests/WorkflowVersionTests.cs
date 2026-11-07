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

    [Fact]
    public void Publish_succeeds_and_sets_published_and_updated_timestamps()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        _clock.Advance(TimeSpan.FromHours(1));

        version.Publish(new KahnDagValidator(), _clock);

        var expectedTimestamp = new DateTimeOffset(_clock.UtcNow);
        Assert.True(version.IsPublished);
        Assert.Equal(expectedTimestamp, version.PublishedAt);
        Assert.Equal(expectedTimestamp, version.UpdatedAt);
    }

    [Fact]
    public void Publish_rejects_already_published_version()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        version.Publish(new KahnDagValidator(), _clock);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            version.Publish(new KahnDagValidator(), _clock));

        Assert.Contains("already published", exception.Message);
    }

    [Fact]
    public void Publish_rejects_version_with_validation_errors()
    {
        var cyclicDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = "a", Type = "Log", DependsOn = ["b"] },
                new StepDefinition { Id = "b", Type = "Log", DependsOn = ["a"] }
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

        var exception = Assert.Throws<InvalidOperationException>(() =>
            version.Publish(new KahnDagValidator(), _clock));

        Assert.Contains("validation errors", exception.Message);
        Assert.False(version.IsPublished);
    }

    [Fact]
    public void Publish_rejects_empty_workflow_definition()
    {
        var emptyDefinition = new WorkflowDefinition
        {
            Steps = []
        };

        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            emptyDefinition,
            _validator,
            _clock);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            version.Publish(new KahnDagValidator(), _clock));

        Assert.Contains("empty workflow definition", exception.Message);
        Assert.False(version.IsPublished);
    }

    [Fact]
    public void Publish_rejects_null_clock()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        Assert.Throws<ArgumentNullException>(() =>
            version.Publish(new KahnDagValidator(), null!));
    }
    [Fact]
    public void Publish_rejects_null_validator()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        Assert.Throws<ArgumentNullException>(() =>
            version.Publish(null!, _clock));
    }
    [Fact]
    public void UpdateDraft_updates_definition_and_advances_updated_at()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        _clock.Advance(TimeSpan.FromMinutes(15));
        var updatedDefinition = CreateSampleDefinition("step-2");

        version.UpdateDraft(updatedDefinition, _validator, _clock);

        Assert.Equal(new DateTimeOffset(_clock.UtcNow), version.UpdatedAt);
        var retrieved = version.GetDefinition();
        Assert.Single(retrieved.Steps);
        Assert.Equal("step-2", retrieved.Steps[0].Id);
    }

    [Fact]
    public void UpdateDraft_with_identical_definition_does_not_advance_updated_at()
    {
        var definition = CreateSampleDefinition("step-1");
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            definition,
            _validator,
            _clock);

        _clock.Advance(TimeSpan.FromMinutes(15));
        version.UpdateDraft(definition, _validator, _clock);

        Assert.Null(version.UpdatedAt);
    }

    [Fact]
    public void UpdateDraft_recomputes_validation_errors()
    {
        var cyclicDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = "a", Type = "Log", DependsOn = ["b"] },
                new StepDefinition { Id = "b", Type = "Log", DependsOn = ["a"] }
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

        Assert.NotNull(version.ValidationErrorsJson);

        // Fix the cycle
        var validDefinition = CreateSampleDefinition("step-fixed");
        version.UpdateDraft(validDefinition, _validator, _clock);

        Assert.Null(version.ValidationErrorsJson);
    }

    [Fact]
    public void UpdateDraft_rejects_already_published_version()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        version.Publish(_validator, _clock);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            version.UpdateDraft(CreateSampleDefinition("step-2"), _validator, _clock));

        Assert.Contains("Cannot update a published workflow version", exception.Message);
    }

    [Fact]
    public void UpdateDraft_rejects_unsupported_schema_version()
    {
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        Assert.Throws<NotSupportedException>(() =>
            version.UpdateDraft(
                CreateSampleDefinition("step-2"),
                _validator,
                _clock,
                definitionSchemaVersion: 99));
    }

    [Fact]
    public void Publish_after_UpdateDraft_validates_final_definition_and_rejects_invalid_update()
    {
        // 1. Start with a valid draft
        var version = WorkflowVersion.CreateDraft(
            Guid.NewGuid(),
            WorkspaceId,
            WorkflowId,
            versionNumber: 1,
            CreateSampleDefinition("step-1"),
            _validator,
            _clock);

        // 2. Update draft to a cyclic definition
        var cyclicDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = "a", Type = "Log", DependsOn = ["b"] },
                new StepDefinition { Id = "b", Type = "Log", DependsOn = ["a"] }
            ]
        };
        version.UpdateDraft(cyclicDefinition, _validator, _clock);

        // 3. Publish must re-validate the current snapshot and reject it
        var exception = Assert.Throws<InvalidOperationException>(() =>
            version.Publish(_validator, _clock));

        Assert.Contains("validation errors", exception.Message);
        Assert.False(version.IsPublished);
    }

    [Fact]
    public void Publish_after_UpdateDraft_succeeds_when_initially_invalid_draft_is_corrected()
    {
        // 1. Start with an invalid draft
        var cyclicDefinition = new WorkflowDefinition
        {
            Steps =
            [
                new StepDefinition { Id = "a", Type = "Log", DependsOn = ["b"] },
                new StepDefinition { Id = "b", Type = "Log", DependsOn = ["a"] }
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

        // 2. Correct the draft
        version.UpdateDraft(CreateSampleDefinition("step-ok"), _validator, _clock);

        // 3. Publish should succeed
        version.Publish(_validator, _clock);

        Assert.True(version.IsPublished);
        Assert.Null(version.ValidationErrorsJson);
    }


}
