// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Mediator.Behaviors;

/// <summary>
/// Logs Mediator requests that take longer than the configured critical threshold.
/// </summary>
[PublicAPI]
public sealed class CriticalRequestLoggingBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    ILogger<CriticalRequestLoggingBehavior<TMessage, TResponse>> logger
) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    private static readonly TimeSpan _CriticalThreshold = TimeSpan.FromSeconds(1);

    private readonly ICurrentUser _currentUser = Argument.IsNotNull(currentUser);
    private readonly ILogger<CriticalRequestLoggingBehavior<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);

    /// <summary>
    /// Executes the next handler in the pipeline and emits a warning-level log entry when the
    /// elapsed time exceeds the critical threshold (1 second).
    /// </summary>
    /// <param name="message">The Mediator message being processed.</param>
    /// <param name="next">The delegate that invokes the next pipeline stage or the final handler.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>The response produced by the downstream pipeline.</returns>
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken
    )
    {
        var timestamp = Stopwatch.GetTimestamp();
        var response = await next.Invoke(message, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        if (elapsed >= _CriticalThreshold)
        {
            _LogMediatorSlowResponse(
                _logger,
                userId: _currentUser.UserId?.ToString(),
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
