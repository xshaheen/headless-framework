// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Hosting;

/// <summary>
/// The startup metrics of the host, registered on <see cref="HostingDiagnostics" />. Attributes are namespaced
/// <c>headless.hosting.*</c>, plus the OpenTelemetry <c>error.type</c>.
/// </summary>
/// <remarks>
/// What is never recorded: exception messages, configuration values, and anything a validator or warm-up reads. The
/// name attributes come from code (a validator's type, a warm-up's <see cref="IWarmup.Name" />), so the set of values
/// stays as small as the set of registrations. The attempt attribute is capped at <see cref="MaxAttemptTag" /> because a
/// warm-up retries until it succeeds.
/// </remarks>
internal static class HostingTelemetry
{
    internal const string StartupValidatorDurationName = "headless.hosting.startup_validator.duration";
    internal const string WarmupDurationName = "headless.hosting.warmup.duration";
    internal const string StartDurationName = "headless.hosting.start.duration";
    internal const string ReadyDurationName = "headless.hosting.ready.duration";

    internal const string TagStartupValidatorName = "headless.hosting.startup_validator.name";
    internal const string TagStartupValidatorOutcome = "headless.hosting.startup_validator.outcome";
    internal const string TagWarmupName = "headless.hosting.warmup.name";
    internal const string TagWarmupAttempt = "headless.hosting.warmup.attempt";
    internal const string TagWarmupOutcome = "headless.hosting.warmup.outcome";
    internal const string TagErrorType = "error.type";

    internal const string OutcomeSuccess = "success";
    internal const string OutcomeFailure = "failure";

    /// <summary>The highest attempt number recorded; later attempts record this value.</summary>
    internal const int MaxAttemptTag = 10;

    // A validator is one cheap read (tens of milliseconds); a warm-up or a cold start can take minutes while a
    // dependency is unreachable, and the warm-up back-off alone reaches 30 seconds per retry.
    private static readonly double[] _DurationBoundaries =
    [
        1,
        5,
        10,
        50,
        100,
        500,
        1_000,
        5_000,
        10_000,
        30_000,
        60_000,
        120_000,
        300_000,
        600_000,
    ];

    // The OS process start, converted once to a monotonic timestamp so later readings do not follow wall-clock jumps.
    // Null where the platform does not report it.
    private static readonly long? _ProcessStartTimestamp = _ReadProcessStartTimestamp();

    private static readonly Histogram<double> _StartupValidatorDuration = _CreateHistogram(
        StartupValidatorDurationName,
        "Duration of each startup validator run."
    );

    private static readonly Histogram<double> _WarmupDuration = _CreateHistogram(
        WarmupDurationName,
        "Duration of each warm-up attempt."
    );

    private static readonly Histogram<double> _StartDuration = _CreateHistogram(
        StartDurationName,
        "Time from the operating-system process start, runtime startup included, until the host reported it started."
    );

    private static readonly Histogram<double> _ReadyDuration = _CreateHistogram(
        ReadyDurationName,
        "Time from the operating-system process start, runtime startup included, until every warm-up that gates "
            + "readiness finished."
    );

    /// <summary>Records one startup validator run that ended with <paramref name="fault" />, or passed when it is null.</summary>
    internal static void StartupValidatorCompleted(Type validatorType, TimeSpan elapsed, Exception? fault)
    {
        if (!_StartupValidatorDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagStartupValidatorName, _TypeName(validatorType) },
            { TagStartupValidatorOutcome, fault is null ? OutcomeSuccess : OutcomeFailure },
        };

        if (fault is not null)
        {
            tags.Add(TagErrorType, _ErrorType(fault));
        }

        _StartupValidatorDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    /// <summary>Records one warm-up attempt that ended with <paramref name="fault" />, or succeeded when it is null.</summary>
    internal static void WarmupAttemptCompleted(string warmup, int attempt, TimeSpan elapsed, Exception? fault)
    {
        if (!_WarmupDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagWarmupName, warmup },
            { TagWarmupAttempt, Math.Min(attempt, MaxAttemptTag) },
            { TagWarmupOutcome, fault is null ? OutcomeSuccess : OutcomeFailure },
        };

        if (fault is not null)
        {
            tags.Add(TagErrorType, _ErrorType(fault));
        }

        _WarmupDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    /// <summary>Records the time from process start until the host started.</summary>
    internal static void HostStarted() => _RecordSinceProcessStart(_StartDuration);

    /// <summary>Records the time from process start until the warm-up readiness check first passed.</summary>
    internal static void HostReady() => _RecordSinceProcessStart(_ReadyDuration);

    private static void _RecordSinceProcessStart(Histogram<double> histogram)
    {
        if (!histogram.Enabled || _ProcessStartTimestamp is not { } started)
        {
            return;
        }

        histogram.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static Histogram<double> _CreateHistogram(string name, string description)
    {
        return HostingDiagnostics.Meter.CreateHistogram(
            name,
            unit: "ms",
            description: description,
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = _DurationBoundaries }
        );
    }

    private static long? _ReadProcessStartTimestamp()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var sinceStart = DateTimeOffset.UtcNow - new DateTimeOffset(process.StartTime.ToUniversalTime());

            return Stopwatch.GetTimestamp() - (long)(sinceStart.TotalSeconds * Stopwatch.Frequency);
        }
#pragma warning disable ERP022 // A platform that cannot report the process start only loses the two start metrics; the host must not fail.
        catch (Exception exception)
            when (exception
                    is PlatformNotSupportedException
                        or NotSupportedException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return null;
        }
#pragma warning restore ERP022
    }

    // The generic definition's name, so a closed generic does not carry assembly-qualified type arguments.
    private static string _TypeName(Type type)
    {
        var named = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;

        return named.FullName ?? named.Name;
    }

    private static string _ErrorType(Exception fault)
    {
        var type = fault.GetType();

        return type.FullName ?? type.Name;
    }
}
