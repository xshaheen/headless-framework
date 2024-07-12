using Framework.Api.Core.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Mediator;

public sealed class ApiRequestLoggingBehavior<TMessage, TResponse> : MessagePreProcessor<TMessage, TResponse>
    where TMessage : IMessage
{
    private readonly IRequestContext _requestContext;
    private readonly ILogger<ApiRequestLoggingBehavior<TMessage, TResponse>> _logger;

    public ApiRequestLoggingBehavior(
        IRequestContext requestContext,
        ILogger<ApiRequestLoggingBehavior<TMessage, TResponse>> logger
    )
    {
        _requestContext = requestContext;
        _logger = logger;
    }

    protected override ValueTask Handle(TMessage message, CancellationToken cancellationToken)
    {
        _logger.LogMediatorMessage(
            userId: _requestContext.UserId,
            messageName: typeof(TMessage).Name,
            message: message
        );

        return ValueTask.CompletedTask;
    }
}

file static class LoggerExtensions
{
    private static readonly Action<ILogger, string?, string, object, Exception?> _Log = LoggerMessage.Define<
        string?,
        string,
        object
    >(LogLevel.Debug, new EventId(1, "Mediator:Message"), "[Mediator:Message] {UserId} {MessageName} {@Message}");

    public static void LogMediatorMessage(this ILogger logger, string? userId, string messageName, object message)
    {
        _Log(logger, userId, messageName, message, null);
    }
}
