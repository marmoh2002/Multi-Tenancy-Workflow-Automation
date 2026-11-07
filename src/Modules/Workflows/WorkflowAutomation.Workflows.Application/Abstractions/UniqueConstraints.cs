namespace WorkflowAutomation.Workflows.Application.Abstractions;

public static class UniqueConstraints
{
    public const string WorkflowName = "IX_Workflows_WorkspaceId_Name";
    public const string WorkflowVersionNumber = "IX_WorkflowVersions_WorkflowId_VersionNumber";
}