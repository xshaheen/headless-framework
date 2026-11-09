// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Headless.Mediator.Behaviors;

/// <summary>
/// Logs Mediator responses after their handlers execute. Registered as an open-generic pipeline
/// behavior by <see cref="Headless.Mediator.SetupMediator"/>; not intended for direct consumer use.
/// </summary>
internal sealed class ResponseLoggingBehavior<TMessage, TResponse>(
    ICurrentUser currentUser,
    ILogger<ResponseLoggingBehavior<TMessage, TResponse>> logger,
    MediatorPayloadLogging payloadLogging
) : MessagePostProcessor<TMessage, TResponse>
    where TMessage : IRequest<TResponse>
{
    private readonly ICurrentUser _currentUser = Argument.IsNotNull(currentUser);
    private readonly ILogger<ResponseLoggingBehavior<TMessage, TResponse>> _logger = Argument.IsNotNull(logger);
    private readonly bool _logPayloads = Argument.IsNotNull(payloadLogging).Enabled;

    /// <summary>
    /// Emits a debug-level log entry for the message and its response after the handler executes.
    /// </summary>
    /// <param name="message">The Mediator message that was processed.</param>
    /// <param name="response">The response returned by the handler.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A completed <see cref="ValueTask"/> because logging is synchronous.</returns>
    protected override ValueTask Handle(TMessage message, TResponse response, CancellationToken cancellationToken)
    {
        // Gate before building args: LoggerMessage.Define checks IsEnabled inside the delegate, but C#
        // evaluates _currentUser.UserId?.ToString() (a string allocation) first. Skip it when Debug is off.
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return ValueTask.CompletedTask;
        }

        var userId = _currentUser.UserId?.ToString();

        if (_logPayloads)
        {
            _LogWithPayload(
                _logger,
                userId,
                typeof(TMessage).Name,
                message,
                typeof(TResponse).Name,
                response,
                arg7: null
            );
        }
        else
        {
            _Log(_logger, userId, typeof(TMessage).Name, typeof(TResponse).Name, arg5: null);
        }

        return ValueTask.CompletedTask;
    }

    #region Logs

    private static readonly Action<ILogger, string?, string, string, Exception?> _Log = LoggerMessage.Define<
        string?,
        string,
        string
    >(LogLevel.Debug, new EventId(2, "Mediator:Response"), "[Mediator:Response] {UserId} {MessageName} {ResponseName}");

    private static readonly Action<ILogger, string?, string, object, string, object?, Exception?> _LogWithPayload =
        LoggerMessage.Define<string?, string, object, string, object?>(
            LogLevel.Debug,
            new EventId(2, "Mediator:Response"),
            "[Mediator:Response] {UserId} {MessageName} {@Message} {ResponseName} {@Response}"
        );

    #endregion
}
