using WorkflowAutomation.Shared.Kernel.Primitives;

namespace WorkflowAutomation.Workflows.Application.Errors;

public sealed record ValidationError(
    string Code,
    string Description,
    IReadOnlyDictionary<string, string[]> Failures)
    : Error(Code, Description, ErrorType.Validation);