// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

public sealed class InfiniteRetryProcessor(IProcessor inner, ILoggerFactory loggerFactory) : IProcessor
{
    // Exponential backoff for processor-level crashes (not message-level retries).
    // Starts at 1 s, doubles on each consecutive failure, caps at 60 s.
    // Resets to the initial value after a successful iteration so a recovered processor
    // does not carry forward a large delay from a past outage.
    private static readonly TimeSpan _InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _MaxBackoff = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger = loggerFactory.CreateLogger<InfiniteRetryProcessor>();

    public async Task ProcessAsync(ProcessingContext context)
    {
        var backoff = _InitialBackoff;

        while (!context.IsStopping)
        {
            try
            {
                await inner.ProcessAsync(context).ConfigureAwait(false);

                // Successful iteration — reset backoff so the next failure starts fresh.
                backoff = _InitialBackoff;
            }
            catch (OperationCanceledException)
            {
                //ignore
            }
            catch (Exception ex)
            {
                _logger.LogProcessorFailedRetrying(ex, inner.ToString(), (long)backoff.TotalSeconds);
                await context.WaitAsync(backoff).ConfigureAwait(false);

                // Double delay for next failure, capped at MaxBackoff.
                var nextMs = Math.Min(backoff.TotalMilliseconds * 2, _MaxBackoff.TotalMilliseconds);
                backoff = TimeSpan.FromMilliseconds(nextMs);
            }
        }
    }

    public override string? ToString()
    {
        return inner.ToString();
    }
}

internal static partial class InfiniteRetryProcessorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ProcessorFailedRetrying",
        Level = LogLevel.Warning,
        Message = "Processor '{ProcessorName}' failed. Retrying in {BackoffSeconds}s..."
    )]
    public static partial void LogProcessorFailedRetrying(
        this ILogger logger,
        Exception exception,
        string? processorName,
        long backoffSeconds
    );
}
