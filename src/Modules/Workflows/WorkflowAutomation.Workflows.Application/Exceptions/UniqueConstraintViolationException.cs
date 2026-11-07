namespace WorkflowAutomation.Workflows.Application.Exceptions;

public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(
        string constraintName,
        Exception? innerException = null)
        : base($"A unique constraint was violated: {constraintName}.", innerException)
    {
        ConstraintName = constraintName;
    }

    public string ConstraintName { get; }
}