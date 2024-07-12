using Framework.Api.Core.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Mediator;

public sealed class ApiResponseLoggingBehavior<TMessage, TResponse> : MessagePostProcessor<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    private readonly IRequestContext _requestContext;
    private readonly ILogger<ApiResponseLoggingBehavior<TMessage, TResponse>> _logger;

    public ApiResponseLoggingBehavior(
        IRequestContext requestContext,
        ILogger<ApiResponseLoggingBehavior<TMessage, TResponse>> logger
    )
    {
        _requestContext = requestContext;
        _logger = logger;
    }

    protected override ValueTask Handle(TMessage message, TResponse response, CancellationToken cancellationToken)
    {
        _logger.LogMediatorResponse(
            userId: _requestContext.UserId,
            messageName: typeof(TMessage).Name,
            message: message,
            responseName: typeof(TResponse).Name,
            response: response
        );

        return ValueTask.CompletedTask;
    }
}

file static class LoggerExtensions
{
    private static readonly Action<ILogger, string?, string, object, string, object?, Exception?> _Log =
        LoggerMessage.Define<string?, string, object, string, object?>(
            LogLevel.Debug,
            new EventId(2, "Mediator:Response"),
            "[Mediator:Response] {UserId} {MessageName} {@Message} {ResponseName} {@Response}"
        );

    public static void LogMediatorResponse(
        this ILogger logger,
        string? userId,
        string messageName,
        object message,
        string responseName,
        object? response
    )
    {
        _Log(logger, userId, messageName, message, responseName, response, null);
    }
}
