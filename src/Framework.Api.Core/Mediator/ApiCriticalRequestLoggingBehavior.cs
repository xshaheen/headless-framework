using System.Diagnostics;
using Framework.Api.Core.Abstractions;
using Humanizer;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Mediator;

public sealed class ApiCriticalRequestLoggingBehavior<TMessage, TResponse>(
    IRequestContext requestContext,
    ILogger<ApiCriticalRequestLoggingBehavior<TMessage, TResponse>> logger
) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken
    )
    {
        var timestamp = Stopwatch.GetTimestamp();
        var response = await next.Invoke(message, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        if (elapsed >= 1.Seconds())
        {
            _LogMediatorSlowResponse(
                logger,
                userId: requestContext.User.UserId,
                elapsed: elapsed,
                messageName: typeof(TMessage).Name,
                message: message,
                responseName: typeof(TResponse).Name,
                response: response
            );
        }

        return response;
    }

    #region Logger

    private static readonly Action<ILogger, string?, TimeSpan, string, object, string, object?, Exception?> _Log =
        LoggerMessage.Define<string?, TimeSpan, string, object, string, object?>(
            LogLevel.Warning,
            new EventId(3, "Mediator:SlowMessage"),
            "[Mediator:SlowMessage] {UserId} {Elapsed}ms {MessageName} {@Message} {ResponseName} {@Response}"
        );

    private static void _LogMediatorSlowResponse(
        ILogger logger,
        string? userId,
        TimeSpan elapsed,
        string messageName,
        object message,
        string responseName,
        object? response
    )
    {
        _Log(logger, userId, elapsed, messageName, message, responseName, response, arg8: null);
    }

    #endregion
}
