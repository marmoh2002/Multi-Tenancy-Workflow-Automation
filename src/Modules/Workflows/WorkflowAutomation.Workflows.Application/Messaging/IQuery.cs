namespace WorkflowAutomation.Workflows.Application.Messaging;

using MediatR;
using WorkflowAutomation.Shared.Kernel.Primitives;


public interface IQuery<TResponse> : IRequest<Result<TResponse>>;