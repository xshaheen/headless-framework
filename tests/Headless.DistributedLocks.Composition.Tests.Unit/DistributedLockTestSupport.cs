// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Shared helpers for the distributed-lock unit tests: a capturing logger that works over
/// internal provider type args, metric <c>reason</c>-tag capture, a busy drain, and a parked
/// (non-spinning) wait for FakeTimeProvider-driven background work. Lives in the enclosing
/// <c>Tests</c> namespace so every <c>Tests.*</c> test class can reference it without a using.
/// </summary>
internal static class DistributedLockTestSupport
{
    /// <summary>EventId of <c>RegularLockLoggerExtensions.LogTryOnceSafetyDeadlineFired</c>.</summary>
    public const int SafetyDeadlineFiredEventId = 24;

    /// <summary>EventId of <c>RegularLockLoggerExtensions.LogFailedToAcquireLockAfter</c> (routine contention).</summary>
    public const int FailedToAcquireLockAfterEventId = 13;

    /// <summary>Mirrors <c>_NonBlockingAcquireDeadline</c> in the providers (seconds).</summary>
    public const int NonBlockingAcquireDeadlineSeconds = 10;

    public static async Task DrainUntilAsync(Func<bool> condition, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Captures the namespaced reason tag values (<c>headless.lock.reason</c> /
    /// <c>headless.semaphore.reason</c>) recorded on the named failure counter
    /// (<c>headless.lock.failed</c> or <c>headless.semaphore.failed</c>). The instruments are
    /// process-wide and shared across parallel tests, so assert with <c>Contain</c>, not
    /// <c>ContainSingle</c>; exclusivity belongs on an isolated capturing logger.
    /// </summary>
    public static List<string> CaptureFailedReasons(MeterListener listener, string instrumentName)
    {
        var reasons = new List<string>();
        // `headless.lock.failed` carries `headless.lock.reason`; same shape for the semaphore counter.
        var tagName = instrumentName.Replace(".failed", ".reason", StringComparison.Ordinal);

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (
                string.Equals(instrument.Meter.Name, "Headless.DistributedLocks", StringComparison.Ordinal)
                && string.Equals(instrument.Name, instrumentName, StringComparison.Ordinal)
            )
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<int>(
            (_, _, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Value is string reason && string.Equals(tag.Key, tagName, StringComparison.Ordinal))
                    {
                        lock (reasons)
                        {
                            reasons.Add(reason);
                        }
                    }
                }
            }
        );

        listener.Start();

        return reasons;
    }

    /// <summary>
    /// An <see cref="ILogger{T}"/> that records logged EventIds. Works for internal provider type args
    /// (NSubstitute cannot proxy <c>ILogger&lt;internal-type&gt;</c> via Castle DynamicProxy).
    /// </summary>
    public sealed class CapturingLogger<T>(List<int> eventIds) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (eventIds)
            {
                eventIds.Add(eventId.Id);
            }
        }
    }
}
