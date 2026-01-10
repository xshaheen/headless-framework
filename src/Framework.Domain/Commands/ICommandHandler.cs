using MediatR;

namespace Framework.Domain.Commands;

/// <summary>
/// Handler for commands without result. Compatible with MediatR.IRequestHandler.
/// </summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

/// <summary>
/// Handler for commands with result. Compatible with MediatR.IRequestHandler.
/// </summary>
public interface ICommandHandler<TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;
