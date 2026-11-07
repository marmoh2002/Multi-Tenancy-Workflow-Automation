using WorkflowAutomation.Shared.Kernel.Primitives;

namespace WorkflowAutomation.Workflows.Application.Errors;

public sealed record DraftConflictError(
    string Code,
    string Description,
    string ConflictReason,
    Guid CurrentVersionId,
    int CurrentVersionNumber,
    DateTimeOffset? CurrentUpdatedAt,
    DateTimeOffset? CurrentPublishedAt,
    string CurrentDefinitionJson)
    : Error(Code, Description, ErrorType.Conflict)
{
    public static DraftConflictError DraftModifiedByAnotherUser(
        Guid versionId,
        int versionNumber,
        DateTimeOffset? updatedAt,
        string currentDefinitionJson) =>
        new(
            Code: "Workflows.DraftConflict",
            Description: "The draft was modified by another user.",
            ConflictReason: nameof(DraftModifiedByAnotherUser),
            CurrentVersionId: versionId,
            CurrentVersionNumber: versionNumber,
            CurrentUpdatedAt: updatedAt,
            CurrentPublishedAt: null,
            CurrentDefinitionJson: currentDefinitionJson);

    public static DraftConflictError VersionPublishedByAnotherUser(
        Guid versionId,
        int versionNumber,
        DateTimeOffset? updatedAt,
        DateTimeOffset? publishedAt,
        string currentDefinitionJson) =>
        new(
            Code: "Workflows.DraftConflict",
            Description: "The draft was published by another user.",
            ConflictReason: nameof(VersionPublishedByAnotherUser),
            CurrentVersionId: versionId,
            CurrentVersionNumber: versionNumber,
            CurrentUpdatedAt: updatedAt,
            CurrentPublishedAt: publishedAt,
            CurrentDefinitionJson: currentDefinitionJson);
}