using Framework.Api.Core.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Mediator;

public sealed class ApiResponseLoggingBehavior<TMessage, TResponse>(
    IRequestContext requestContext,
    ILogger<ApiResponseLoggingBehavior<TMessage, TResponse>> logger
) : MessagePostProcessor<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    protected override ValueTask Handle(TMessage message, TResponse response, CancellationToken cancellationToken)
    {
        _LogMediatorResponse(
            logger,
            userId: requestContext.User.UserId,
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
        _Log(logger, userId, messageName, message, responseName, response, null);
    }

    #endregion
}
