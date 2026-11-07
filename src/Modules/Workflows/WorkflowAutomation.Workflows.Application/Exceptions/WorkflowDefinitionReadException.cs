namespace WorkflowAutomation.Workflows.Application.Exceptions;

public sealed class WorkflowDefinitionReadException : Exception
{
    public WorkflowDefinitionReadException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}