// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

public sealed class InfiniteRetryProcessor(IProcessor inner, ILoggerFactory loggerFactory) : IProcessor
{
    // Exponential backoff for processor-level crashes (not message-level retries).
    // Starts at 1 s, doubles on each consecutive failure, caps at 60 s, and adds 0-25% jitter
    // so replicas that failed together do not retry in lockstep against a recovering dependency.
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
                await context.WaitAsync(_WithJitter(backoff)).ConfigureAwait(false);

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

    private static TimeSpan _WithJitter(TimeSpan delay)
    {
#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var jitterMs = Random.Shared.Next(0, (int)Math.Max(1, delay.TotalMilliseconds / 4));
#pragma warning restore CA5394
        return delay + TimeSpan.FromMilliseconds(jitterMs);
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
