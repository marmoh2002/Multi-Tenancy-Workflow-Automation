using WorkflowAutomation.Workflows.Core.Definitions;

namespace WorkflowAutomation.Workflows.Core;

public interface IDagValidator
{
    DagValidationResult Validate(WorkflowDefinition workflow);
}