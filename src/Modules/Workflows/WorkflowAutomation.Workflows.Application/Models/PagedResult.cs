namespace WorkflowAutomation.Workflows.Application.Models;

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }

    public int TotalPages => PageSize == 0
        ? 0
        : (TotalCount + PageSize - 1) / PageSize;
}