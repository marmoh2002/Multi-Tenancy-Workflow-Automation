namespace WorkflowAutomation.Shared.Contracts.Execution;


// Message-contract execution source. Kept separate from the domain enum
// so Shared.Contracts does not reference Workflows.Core. The Application layer
// maps this type to WorkflowAutomation.Workflows.Core.Enums.WorkflowExecutionSource.
public enum ExecutionSource
{
    Manual = 1,
    Scheduled = 2,
    Webhook = 3
}
