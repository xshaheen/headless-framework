// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

public sealed partial class HybridCache
{
    private readonly DistributedCacheCircuitBreaker _distributedCircuit = new(
        options.DistributedCacheCircuitBreakerDuration,
        timeProvider ?? TimeProvider.System
    );

    private TimeSpan _SelectDistributedReadTimeout(bool hasLocalFallback, bool softCanDegradeToMiss)
    {
        var timeout = Timeout.InfiniteTimeSpan;

        if (
            options.DistributedCacheSoftTimeout != Timeout.InfiniteTimeSpan
            && (hasLocalFallback || softCanDegradeToMiss)
        )
        {
            timeout = options.DistributedCacheSoftTimeout;
        }

        if (
            options.DistributedCacheHardTimeout != Timeout.InfiniteTimeSpan
            && (timeout == Timeout.InfiniteTimeSpan || options.DistributedCacheHardTimeout < timeout)
        )
        {
            timeout = options.DistributedCacheHardTimeout;
        }

        return timeout;
    }

    private async ValueTask<DistributedCacheReadResult<T>> _ReadFromL2Async<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> operation,
        TimeSpan timeout,
        DistributedCacheTimeoutKind timeoutKind,
        CancellationToken cancellationToken
    )
    {
        if (!_IsDistributedCacheCircuitClosed())
        {
            return DistributedCacheReadResult<T>.SkippedByCircuit();
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            try
            {
                return DistributedCacheReadResult<T>.Success(await operation(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
                when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
            {
                _OpenDistributedCacheCircuit(exception, key);
                return DistributedCacheReadResult<T>.Failed(exception);
            }
        }

        CancellationTokenSource? operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationTask = operation(operationCts.Token).AsTask();

        try
        {
            using var delayCts = new CancellationTokenSource();
            var delayTask = Task.Delay(timeout, _timeProvider, delayCts.Token);

            var winner = await Task.WhenAny(operationTask, delayTask).ConfigureAwait(false);

            if (winner == operationTask)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);

                try
                {
                    var value = await operationTask.ConfigureAwait(false);
                    operationCts?.Dispose();
                    operationCts = null;

                    return DistributedCacheReadResult<T>.Success(value);
                }
                catch (Exception exception)
                    when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
                {
                    operationCts?.Dispose();
                    operationCts = null;
                    _OpenDistributedCacheCircuit(exception, key);

                    return DistributedCacheReadResult<T>.Failed(exception);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            await operationCts.CancelAsync().ConfigureAwait(false);
            _DisposeAfter(operationCts, operationTask);
            operationCts = null;
            _logger.LogDistributedCacheReadTimedOut(key, timeout, timeoutKind.ToString());

            return DistributedCacheReadResult<T>.TimedOut();
        }
        finally
        {
            operationCts?.Dispose();
        }
    }

    private bool _IsDistributedCacheCircuitClosed()
    {
        var isClosed = _distributedCircuit.IsClosed(out var changed);

        if (isClosed && changed)
        {
            _logger.LogDistributedCacheCircuitClosed();
        }

        return isClosed;
    }

    private void _OpenDistributedCacheCircuit(Exception exception, string key)
    {
        if (_distributedCircuit.TryOpen(out var changed) && changed)
        {
            _logger.LogDistributedCacheCircuitOpened(exception, key, _distributedCircuit.Duration);
        }
    }

    private static void _DisposeAfter(CancellationTokenSource cts, Task task)
    {
        _ = task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private enum DistributedCacheTimeoutKind
    {
        Soft,
        Hard,
    }

    private enum DistributedCacheReadStatus
    {
        Success,
        Failure,
        TimedOut,
        CircuitOpen,
    }

    private readonly record struct DistributedCacheReadResult<T>(
        DistributedCacheReadStatus Status,
        T? Value,
        Exception? Exception
    )
    {
        public bool IsSuccess => Status == DistributedCacheReadStatus.Success;

        public static DistributedCacheReadResult<T> Success(T value) =>
            new(DistributedCacheReadStatus.Success, value, Exception: null);

        public static DistributedCacheReadResult<T> Failed(Exception exception) =>
            new(DistributedCacheReadStatus.Failure, Value: default, exception);

        public static DistributedCacheReadResult<T> TimedOut() =>
            new(DistributedCacheReadStatus.TimedOut, Value: default, Exception: null);

        public static DistributedCacheReadResult<T> SkippedByCircuit() =>
            new(DistributedCacheReadStatus.CircuitOpen, Value: default, Exception: null);
    }

    private sealed class DistributedCacheCircuitBreaker(TimeSpan duration, TimeProvider timeProvider)
    {
        private const int _Closed = 0;
        private const int _Open = 1;

        private readonly long _durationTicks = duration.Ticks;
        private long _openUntilTicks = DateTimeOffset.MinValue.Ticks;
        private int _state;

        public TimeSpan Duration { get; } = duration;

        public bool TryOpen(out bool changed)
        {
            if (_durationTicks <= 0)
            {
                changed = false;
                return false;
            }

            Interlocked.Exchange(ref _openUntilTicks, timeProvider.GetUtcNow().UtcTicks + _durationTicks);
            var oldState = Interlocked.Exchange(ref _state, _Open);
            changed = oldState == _Closed;

            return true;
        }

        public bool IsClosed(out bool changed)
        {
            changed = false;

            if (_durationTicks <= 0)
            {
                return true;
            }

            if (timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _openUntilTicks))
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _state, _Closed, _Open) == _Open)
            {
                changed = true;
            }

            return true;
        }
    }
}
