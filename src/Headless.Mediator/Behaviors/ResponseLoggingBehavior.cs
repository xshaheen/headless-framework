// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Mediator;

/// <summary>
/// Logs Mediator responses after their handlers execute.
/// </summary>
[PublicAPI]
public sealed class ResponseLoggingBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    ILogger<ResponseLoggingBehavior<TMessage, TResponse>> logger
) : MessagePostProcessor<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    private readonly ICurrentUser _currentUser = Argument.IsNotNull(currentUser);
    private readonly ILogger<ResponseLoggingBehavior<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);

    protected override ValueTask Handle(TMessage message, TResponse response, CancellationToken cancellationToken)
    {
        _LogMediatorResponse(
            _logger,
            userId: _currentUser.UserId?.ToString(),
            messageName: typeof(TMessage).Name,
            message: message,
            responseName: typeof(TResponse).Name,
            response: response
        );

        return ValueTask.CompletedTask;
    }

    #region Logs

    private static readonly Action<ILogger, string?, string, object, string, object?, Exception?> _Log =
        LoggerMessage.Define<string?, string, object, string, object?>(
            LogLevel.Debug,
            new EventId(2, "Mediator:Response"),
            "[Mediator:Response] {UserId} {MessageName} {@Message} {ResponseName} {@Response}"
        );

    private static void _LogMediatorResponse(
        ILogger logger,
        string? userId,
        string messageName,
        object message,
        string responseName,
        object? response
    )
    {
        _Log(logger, userId, messageName, message, responseName, response, arg7: null);
    }

    #endregion
}
