namespace WorkflowAutomation.Workflows.Core.Tests;

using WorkflowAutomation.Workflows.Core.Entities;
using Xunit;

public class WorkflowTests
{
    private readonly TestClock _clock = new();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();

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
        Assert.Equal(_clock.UtcNow, workflow.CreatedAt);
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
}
