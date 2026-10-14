namespace WorkflowAutomation.SharedKernel.Primitives;

public record Error(string Code, string Description, ErrorType Type = ErrorType.Failure){
    
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static readonly Error NullValue = new("Error.NullValue", "A null value was provided",ErrorType.Failure);
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);
}

