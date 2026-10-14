using DagWorkflow.Core.Definitions;

namespace DagWorkflow.Core;

public interface IDagValidator
{
    DagValidationResult Validate(WorkflowDefinition workflow);
}