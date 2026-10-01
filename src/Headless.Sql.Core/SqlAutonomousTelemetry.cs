// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using Headless.UnitOfWork;

namespace Headless.Sql;

/// <summary>
/// The span and metrics of <see cref="SqlAutonomousTransaction" />, registered on <see cref="SqlDiagnostics" />.
/// Framework-owned attributes are namespaced <c>headless.sql.*</c>; the fault attributes reuse the OpenTelemetry
/// <c>error.type</c> and database <c>db.response.status_code</c> conventions. No SQL text, parameter, key, or
/// connection detail is ever recorded, on a span or a metric.
/// </summary>
/// <remarks>
/// Instruments are created directly on the <see cref="Meter" /> so a call can ask <see cref="IsEnabled" /> first: with
/// nothing subscribed, <see cref="SqlAutonomousTransaction" /> takes its original path and pays no allocation, clock
/// read, or tag building.
/// </remarks>
internal static class SqlAutonomousTelemetry
{
    internal const string ActivityName = "sql.autonomous_transaction";
    internal const string RetryEventName = "headless.sql.retry";

    internal const string DurationName = "headless.sql.autonomous.duration";
    internal const string AttemptsName = "headless.sql.autonomous.attempts";
    internal const string RetriesName = "headless.sql.autonomous.retries";

    internal const string TagOutcome = "headless.sql.outcome";
    internal const string TagAttempts = "headless.sql.attempts";
    internal const string TagAttempt = "headless.sql.attempt";
    internal const string TagErrorType = "error.type";
    internal const string TagStatusCode = "db.response.status_code";

    /// <summary>The call returned the result of an attempt.</summary>
    internal const string OutcomeSuccess = "success";

    /// <summary>The last attempt failed with a transient fault before its commit; no attempt is left.</summary>
    internal const string OutcomeRetriesExhausted = "retries_exhausted";

    /// <summary>An attempt failed before its commit with a fault the classifier does not call transient.</summary>
    internal const string OutcomeNonTransient = "non_transient";

    /// <summary>An attempt failed after it marked its commit as started; the fault surfaced unchanged, never retried.</summary>
    internal const string OutcomeCommitFault = "commit_fault";

    /// <summary>The caller cancelled before the commit started.</summary>
    internal const string OutcomeCanceled = "canceled";

    // Store calls are single-digit milliseconds when healthy, while a call that waits out lock contention and two
    // retries reaches seconds; the boundaries are the OpenTelemetry advice for db.client.operation.duration.
    private static readonly Histogram<double> _Duration = SqlDiagnostics.Meter.CreateHistogram(
        DurationName,
        unit: "s",
        description: "Duration of autonomous store calls, every attempt and retry delay included.",
        tags: null,
        advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10],
        }
    );

    // One bucket per possible attempt count, so the histogram reads as an exact distribution.
    private static readonly Histogram<int> _Attempts = SqlDiagnostics.Meter.CreateHistogram(
        AttemptsName,
        unit: "{attempt}",
        description: "Attempts each autonomous store call made, the first one included.",
        tags: null,
        advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [1, 2, 3] }
    );

    private static readonly Counter<long> _Retries = SqlDiagnostics.Meter.CreateCounter<long>(
        RetriesName,
        unit: "{retry}",
        description: "Attempts of autonomous store calls that failed with a transient fault before their commit and were retried."
    );

    // The SQL Server driver reports its error code only as a Number property: it overrides neither SqlState nor
    // IsTransient, and this package references no driver. Cached per type because a fault burst is when lookups add up.
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _NumberProperties = new();

    /// <summary>Whether a span or metric listener is attached; the call skips every observation step when not.</summary>
    internal static bool IsEnabled =>
        SqlDiagnostics.ActivitySource.HasListeners() || _Duration.Enabled || _Attempts.Enabled || _Retries.Enabled;

    /// <summary>
    /// Runs <paramref name="call" /> under a span and records its outcome, duration, and attempt count. Async, so the
    /// span is <see cref="Activity.Current" /> only inside the call and the caller's context never sees it.
    /// </summary>
    internal static async ValueTask<T> ObserveAsync<T>(
        Func<Observer, ValueTask<T>> call,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        using var activity = SqlDiagnostics.ActivitySource.StartActivity(ActivityName, ActivityKind.Internal);
        var observer = new Observer(activity, timeProvider);

        try
        {
            var result = await call(observer).ConfigureAwait(false);
            observer.Complete(OutcomeSuccess, fault: null);

            return result;
        }
        catch (Exception ex)
        {
            observer.Complete(observer.Classify(ex, cancellationToken), ex);

            throw;
        }
    }

    /// <summary>The type the <c>error.type</c> attribute names: the driver exception when there is one, else the thrown one.</summary>
    private static string _ErrorType(Exception fault)
    {
        var type = (_FindDatabaseException(fault) ?? fault).GetType();

        return type.FullName ?? type.Name;
    }

    /// <summary>The database's own code for the fault: the SQLSTATE when the driver reports one, else its error number.</summary>
    private static string? _StatusCode(Exception fault)
    {
        if (_FindDatabaseException(fault) is not { } databaseException)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(databaseException.SqlState))
        {
            return databaseException.SqlState;
        }

        var number = _NumberProperties
            .GetOrAdd(databaseException.GetType(), static type => type.GetProperty("Number", typeof(int)))
            ?.GetValue(databaseException);

        return number is int value ? value.ToString(CultureInfo.InvariantCulture) : null;
    }

    // Outer-first, the walk RelationalTransientFaults classifies with, so the reported fault is the one it judged.
    private static DbException? _FindDatabaseException(Exception fault)
    {
        for (var current = fault; current is not null; current = current.InnerException)
        {
            if (current is DbException databaseException)
            {
                return databaseException;
            }
        }

        return null;
    }

    /// <summary>The observation of one call; the retry loop reports each attempt and each retry to it.</summary>
    internal sealed class Observer(Activity? activity, TimeProvider timeProvider)
    {
        private readonly long _startedAt = timeProvider.GetTimestamp();
        private SqlAutonomousAttempt? _current;
        private int _attempts;

        /// <summary>Records that <paramref name="attempt" /> started.</summary>
        public void AttemptStarted(SqlAutonomousAttempt attempt)
        {
            _current = attempt;
            _attempts++;
        }

        /// <summary>Records that <paramref name="fault" /> is being retried as attempt <paramref name="nextAttempt" />.</summary>
        public void Retrying(Exception fault, int nextAttempt)
        {
            var errorType = _ErrorType(fault);
            var statusCode = _StatusCode(fault);

            if (_Retries.Enabled)
            {
                var tags = new TagList { { TagErrorType, errorType } };

                if (statusCode is not null)
                {
                    tags.Add(TagStatusCode, statusCode);
                }

                _Retries.Add(1, tags);
            }

            if (activity is { IsAllDataRequested: true })
            {
                var eventTags = new ActivityTagsCollection { { TagAttempt, nextAttempt }, { TagErrorType, errorType } };

                if (statusCode is not null)
                {
                    eventTags.Add(TagStatusCode, statusCode);
                }

                activity.AddEvent(new ActivityEvent(RetryEventName, tags: eventTags));
            }
        }

        /// <summary>
        /// Names why the call failed, in the order the retry rule decides it: a commit fault is never retried whatever
        /// it is, a cancellation never counts as transient, and a transient fault that still surfaced ran out of
        /// attempts.
        /// </summary>
        public string Classify(Exception fault, CancellationToken cancellationToken)
        {
            if (_current is { CommitStarted: true })
            {
                return OutcomeCommitFault;
            }

            if (fault is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                return OutcomeCanceled;
            }

            return RelationalTransientFaults.IsTransient(fault, cancellationToken)
                ? OutcomeRetriesExhausted
                : OutcomeNonTransient;
        }

        /// <summary>Ends the span and records the call's duration and attempt count.</summary>
        public void Complete(string outcome, Exception? fault)
        {
            var errorType = fault is null ? null : _ErrorType(fault);
            var tags = new TagList { { TagOutcome, outcome } };

            if (errorType is not null)
            {
                tags.Add(TagErrorType, errorType);
            }

            if (_Duration.Enabled)
            {
                _Duration.Record(timeProvider.GetElapsedTime(_startedAt).TotalSeconds, tags);
            }

            if (_Attempts.Enabled)
            {
                _Attempts.Record(_attempts, tags);
            }

            if (activity is null)
            {
                return;
            }

            activity.SetTag(TagOutcome, outcome);
            activity.SetTag(TagAttempts, _attempts);

            if (errorType is not null)
            {
                activity.SetTag(TagErrorType, errorType);
                // No description: a driver message can quote key values and constraint names.
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }
    }
}
