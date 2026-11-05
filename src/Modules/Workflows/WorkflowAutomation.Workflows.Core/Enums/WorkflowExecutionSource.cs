namespace WorkflowAutomation.Workflows.Core.Enums;


// Domain-owned execution source. Kept separate from the shared contract enum
// so Workflows.Core does not reference Shared.Contracts. The Application layer
// maps Shared.Contracts.Execution.ExecutionSource to this type.
public enum WorkflowExecutionSource
{
    Manual = 1,
    Scheduled = 2,
    Webhook = 3
}
