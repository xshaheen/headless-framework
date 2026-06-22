// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Mediator.Behaviors;

/// <summary>
/// Logs Mediator messages before their handlers execute.
/// </summary>
[PublicAPI]
public sealed class RequestLoggingBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    ILogger<RequestLoggingBehavior<TMessage, TResponse>> logger
) : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly ICurrentUser _currentUser = Argument.IsNotNull(currentUser);
    private readonly ILogger<RequestLoggingBehavior<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);

    /// <summary>
    /// Emits a debug-level log entry for the incoming message before the handler executes.
    /// </summary>
    /// <param name="message">The Mediator message being processed.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A completed <see cref="ValueTask"/> because logging is synchronous.</returns>
    protected override ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        _LogMediatorMessage(
            _logger,
            userId: _currentUser.UserId?.ToString(),
            messageName: typeof(TMessage).Name,
            message: message
        );

        return ValueTask.CompletedTask;
    }

    #region Logs

    private static readonly Action<ILogger, string?, string, object, Exception?> _Log = LoggerMessage.Define<
        string?,
        string,
        object
    >(LogLevel.Debug, new EventId(1, "Mediator:Message"), "[Mediator:Message] {UserId} {MessageName} {@Message}");

    private static void _LogMediatorMessage(ILogger logger, string? userId, string messageName, object message)
    {
        _Log(logger, userId, messageName, message, arg5: null);
    }

    #endregion
}
