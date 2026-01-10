using MediatR;

namespace Framework.Domain.Commands;

/// <summary>
/// Command marker interface (no result, for side effects only).
/// Compatible with MediatR.IRequest.
/// </summary>
public interface ICommand : IRequest;

/// <summary>
/// Command with result. Compatible with MediatR.IRequest{TResult}.
/// </summary>
public interface ICommand<out TResult> : IRequest<TResult>;
