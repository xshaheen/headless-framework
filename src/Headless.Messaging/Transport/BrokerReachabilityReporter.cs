// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// Reports the receive outcomes of a consumer client that retries a lost broker on its own, so the host learns that the
/// broker went away and that it came back.
/// </summary>
/// <remarks>
/// Every failure is reported, because the host counts them in a row. A success is reported only after a failure: the
/// host needs it to end the run, and reporting every poll would put a callback on each receive. A consumer client whose
/// broker library reconnects on its own and never surfaces the outage to <see cref="IConsumerClient.ListeningAsync"/>
/// keeps one reporter and calls it from its receive loop.
/// </remarks>
public sealed class BrokerReachabilityReporter
{
    private int _failureReported;

    /// <summary>Gets whether a failure was reported and no success has ended it yet.</summary>
    public bool HasPendingFailure => Volatile.Read(ref _failureReported) == 1;

    /// <summary>Reports a receive that failed because the broker could not be reached.</summary>
    /// <param name="onLog">The client's log callback, read at the call because the core attaches it after creation.</param>
    /// <param name="exception">The failure, whose message becomes the reported reason.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public void ReportFailure(Action<LogMessageEventArgs>? onLog, Exception exception)
    {
        Argument.IsNotNull(exception);

        ReportFailure(onLog, exception.Message);
    }

    /// <summary>Reports that the broker could not be reached, with no exception to show for it.</summary>
    /// <param name="onLog">The client's log callback, read at the call because the core attaches it after creation.</param>
    /// <param name="reason">What failed, for the host's log.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reason"/> is <see langword="null"/>.</exception>
    public void ReportFailure(Action<LogMessageEventArgs>? onLog, string reason)
    {
        Argument.IsNotNull(reason);

        Volatile.Write(ref _failureReported, 1);
        onLog?.Invoke(new LogMessageEventArgs { LogType = MqLogType.BrokerUnreachable, Reason = reason });
    }

    /// <summary>Reports a receive that reached the broker, once per recovery.</summary>
    /// <param name="onLog">The client's log callback.</param>
    public void ReportSuccess(Action<LogMessageEventArgs>? onLog)
    {
        if (Volatile.Read(ref _failureReported) == 0 || Interlocked.Exchange(ref _failureReported, 0) == 0)
        {
            return;
        }

        onLog?.Invoke(
            new LogMessageEventArgs { LogType = MqLogType.BrokerReachable, Reason = "A receive reached the broker." }
        );
    }
}
