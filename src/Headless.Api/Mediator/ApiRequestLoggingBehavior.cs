// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Api.Mediator;

[PublicAPI]
public sealed class ApiRequestLoggingBehavior<TMessage, TResponse>(
    IRequestContext requestContext,
    ILogger<ApiRequestLoggingBehavior<TMessage, TResponse>> logger
) : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    protected override ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        _LogMediatorMessage(
            logger,
            userId: requestContext.User.UserId,
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
