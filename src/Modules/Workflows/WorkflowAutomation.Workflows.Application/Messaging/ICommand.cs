namespace WorkflowAutomation.Workflows.Application.Messaging;

using MediatR;
using WorkflowAutomation.Shared.Kernel.Primitives;


public interface ICommand<TResponse> : IRequest<Result<TResponse>>;