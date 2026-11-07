using WorkflowAutomation.Shared.Kernel.Primitives;

namespace WorkflowAutomation.Workflows.Application.Errors;

public static class WorkflowErrors
{
    public static Error NotFound() =>
        Error.NotFound("Workflows.NotFound", "The workflow was not found.");

    public static Error VersionNotFound() =>
        Error.NotFound("Workflows.VersionNotFound", "The workflow version was not found.");

    public static Error ExecutionNotFound() =>
        Error.NotFound("Workflows.ExecutionNotFound", "The workflow execution was not found.");

    public static Error WorkspaceContextMissing() =>
        Error.Failure("Workflows.WorkspaceContextMissing", "No workspace context is available.");

    public static Error UserContextMissing() =>
        Error.Failure("Workflows.UserContextMissing", "No user context is available for this operation.");

    public static Error NameRequired() =>
        Error.Validation("Workflows.NameRequired", "Workflow name is required.");

    public static Error NameTooLong() =>
        Error.Validation("Workflows.NameTooLong", "Workflow name cannot exceed 200 characters.");

    public static Error DescriptionTooLong() =>
        Error.Validation("Workflows.DescriptionTooLong", "Workflow description cannot exceed 2,000 characters.");

    public static Error NameAlreadyExists() =>
        Error.Conflict("Workflows.NameAlreadyExists", "A workflow with the same name already exists in this workspace.");

    public static Error DefinitionRequired() =>
        Error.Validation("Workflows.DefinitionRequired", "A workflow definition is required.");

    public static Error DefinitionInvalid() =>
        Error.Validation("Workflows.DefinitionInvalid", "The workflow definition is invalid or cannot be deserialized.");

    public static Error DefinitionNotPublishable() =>
        Error.Validation("Workflows.DefinitionNotPublishable", "The workflow definition is empty or invalid and cannot be published.");

    public static Error DefinitionSchemaVersionUnsupported() =>
        Error.Validation("Workflows.DefinitionSchemaVersionUnsupported", "The workflow definition schema version is not supported.");

    public static Error VersionAlreadyPublished() =>
        Error.Conflict("Workflows.VersionAlreadyPublished", "The workflow version is already published.");

    public static Error VersionNotPublished() =>
        Error.Conflict("Workflows.VersionNotPublished", "The workflow version must be published before it can be selected.");

    public static Error PublishedVersionCannotBeDeleted() =>
        Error.Conflict("Workflows.PublishedVersionCannotBeDeleted", "Published workflow versions cannot be deleted independently.");

    public static Error WorkflowHasNoActiveVersion() =>
        Error.Conflict("Workflows.WorkflowHasNoActiveVersion", "The workflow cannot be activated without an active version.");

    public static Error WorkflowHasNonTerminalExecutions() =>
        Error.Conflict("Workflows.WorkflowHasNonTerminalExecutions", "The workflow has pending or running executions.");

    public static Error VersionNumberAllocationConflict() =>
        Error.Conflict("Workflows.VersionNumberAllocationConflict", "A workflow version number could not be allocated due to a concurrent change.");

    public static Error VersionDefinitionCorrupt() =>
        Error.Failure("Workflows.VersionDefinitionCorrupt", "The stored workflow definition cannot be read.");
}