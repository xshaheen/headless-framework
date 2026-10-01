// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// The spans and metrics of <see cref="SchemaRunner" />, registered on <see cref="SchemaRunnerDiagnostics" />.
/// Attributes are namespaced <c>headless.schema_runner.*</c>, plus the OpenTelemetry <c>error.type</c>.
/// </summary>
/// <remarks>
/// <para>
/// What is never recorded: SQL text, schema names, connection details or the database identity, and the configured
/// object names a feature identity can carry (<c>Sequences:counters</c>). The feature attribute is the identity's
/// leading feature name, which code defines, so the set of values stays as small as the set of features.
/// </para>
/// <para>
/// With nothing subscribed, <see cref="ObserveAsync{T}" /> hands the run a <see langword="null" /> pass, and every
/// emission point in the runner is a null-conditional call that does nothing.
/// </para>
/// </remarks>
internal static class SchemaRunnerTelemetry
{
    internal const string ApplyActivityName = "schema_runner.apply";
    internal const string VerifyActivityName = "schema_runner.verify";
    internal const string LockWaitActivityName = "schema_runner.lock_wait";
    internal const string StepActivityName = "schema_runner.step";

    internal const string DurationName = "headless.schema_runner.duration";
    internal const string LockWaitDurationName = "headless.schema_runner.lock.wait.duration";
    internal const string StepsName = "headless.schema_runner.steps";
    internal const string MismatchesName = "headless.schema_runner.mismatches";
    internal const string AbsorbedRacesName = "headless.schema_runner.absorbed_races";

    internal const string TagMode = "headless.schema_runner.mode";
    internal const string TagOutcome = "headless.schema_runner.outcome";
    internal const string TagDialect = "headless.schema_runner.dialect";
    internal const string TagFeature = "headless.schema_runner.feature";
    internal const string TagStepVersion = "headless.schema_runner.step.version";
    internal const string TagStepOutcome = "headless.schema_runner.step.outcome";
    internal const string TagLockOutcome = "headless.schema_runner.lock.outcome";
    internal const string TagMismatchKind = "headless.schema_runner.mismatch.kind";
    internal const string TagStepsApplied = "headless.schema_runner.steps.applied";
    internal const string TagMismatchCount = "headless.schema_runner.mismatches";
    internal const string TagAbsorbedRaces = "headless.schema_runner.absorbed_races";
    internal const string TagErrorType = "error.type";

    internal const string OutcomeSuccess = "success";
    internal const string OutcomeFailure = "failure";

    internal const string StepOutcomeApplied = "applied";
    internal const string StepOutcomeSkipped = "skipped";

    internal const string LockAcquired = "acquired";
    internal const string LockTimedOut = "timed_out";

    /// <summary>The lock query failed or the wait was cancelled.</summary>
    internal const string LockFailed = "failed";

    // A warm pass is one history read per schema, tens of milliseconds; a cold one can build indexes on populated
    // tables for minutes, and the default lock and command timeouts are two and ten minutes.
    private static readonly double[] _DurationBoundaries = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30, 60, 120, 300, 600];

    private static readonly Histogram<double> _Duration = SchemaRunnerDiagnostics.Meter.CreateHistogram(
        DurationName,
        unit: "s",
        description: "Duration of schema runner apply and verify passes.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = _DurationBoundaries }
    );

    private static readonly Histogram<double> _LockWaitDuration = SchemaRunnerDiagnostics.Meter.CreateHistogram(
        LockWaitDurationName,
        unit: "s",
        description: "Time a schema runner waited for its per-database lock.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = _DurationBoundaries }
    );

    private static readonly Counter<long> _Steps = SchemaRunnerDiagnostics.Meter.CreateCounter<long>(
        StepsName,
        unit: "{step}",
        description: "Schema steps an apply pass ran, or skipped because the history already records them."
    );

    private static readonly Counter<long> _Mismatches = SchemaRunnerDiagnostics.Meter.CreateCounter<long>(
        MismatchesName,
        unit: "{mismatch}",
        description: "Disagreements between a schema history table and the registered steps, by kind."
    );

    private static readonly Counter<long> _AbsorbedRaces = SchemaRunnerDiagnostics.Meter.CreateCounter<long>(
        AbsorbedRacesName,
        unit: "{race}",
        description: "Steps re-run because a creator outside the runner committed the same object first."
    );

    /// <summary>Whether a span or metric listener is attached.</summary>
    internal static bool IsEnabled =>
        SchemaRunnerDiagnostics.ActivitySource.HasListeners()
        || _Duration.Enabled
        || _LockWaitDuration.Enabled
        || _Steps.Enabled
        || _Mismatches.Enabled
        || _AbsorbedRaces.Enabled;

    /// <summary>
    /// Runs one pass, handing it a <see cref="Pass" /> to report to when anything listens and <see langword="null" />
    /// otherwise.
    /// </summary>
    internal static Task<T> ObserveAsync<T>(SchemaRunnerMode mode, TimeProvider timeProvider, Func<Pass?, Task<T>> run)
    {
        return IsEnabled ? _ObserveAsync(mode, timeProvider, run) : run(null);
    }

    // Async so the pass span is Activity.Current only inside the pass, never in the caller's context.
    private static async Task<T> _ObserveAsync<T>(
        SchemaRunnerMode mode,
        TimeProvider timeProvider,
        Func<Pass?, Task<T>> run
    )
    {
        var modeName = _ModeName(mode);
        using var activity = SchemaRunnerDiagnostics.ActivitySource.StartActivity(
            mode == SchemaRunnerMode.Verify ? VerifyActivityName : ApplyActivityName,
            ActivityKind.Internal
        );
        activity?.SetTag(TagMode, modeName);

        var pass = new Pass(activity, modeName, timeProvider);

        try
        {
            var result = await run(pass).ConfigureAwait(false);
            pass.Complete(fault: null);

            return result;
        }
        catch (Exception ex)
        {
            pass.Complete(ex);

            throw;
        }
    }

    private static string _ModeName(SchemaRunnerMode mode)
    {
        return mode == SchemaRunnerMode.Verify ? "verify" : "apply";
    }

    private static string _KindName(SchemaMismatchKind kind)
    {
        return kind switch
        {
            SchemaMismatchKind.Missing => "missing",
            SchemaMismatchKind.Unknown => "unknown",
            SchemaMismatchKind.Checksum => "checksum",
            _ => "other",
        };
    }

    // A feature identity appends configured object names to the feature (Sequences:counters); only the feature
    // name is fixed by code.
    private static string _FeatureName(string featureId)
    {
        var separator = featureId.IndexOf(':', StringComparison.Ordinal);

        return separator < 0 ? featureId : featureId[..separator];
    }

    private static string _ErrorType(Exception fault)
    {
        var type = fault.GetType();

        return type.FullName ?? type.Name;
    }

    /// <summary>One apply or verify pass; the runner reports to it as it goes.</summary>
    internal sealed class Pass(Activity? activity, string mode, TimeProvider timeProvider)
    {
        private readonly long _startedAt = timeProvider.GetTimestamp();
        private int _stepsApplied;
        private int _mismatches;
        private int _absorbedRaces;

        /// <summary>Records history disagreements found in one database.</summary>
        public void MismatchesFound(string dialect, IEnumerable<SchemaMismatch> mismatches)
        {
            foreach (var mismatch in mismatches)
            {
                _mismatches++;

                if (_Mismatches.Enabled)
                {
                    _Mismatches.Add(
                        1,
                        new TagList
                        {
                            { TagMode, mode },
                            { TagDialect, dialect },
                            { TagFeature, _FeatureName(mismatch.Feature) },
                            { TagMismatchKind, _KindName(mismatch.Kind) },
                        }
                    );
                }
            }
        }

        /// <summary>Records that every step of <paramref name="contributions" /> was already recorded.</summary>
#pragma warning disable CA1822 // Instance members keep every emission point a null-conditional call on the pass.
        public void StepsSkipped(string dialect, IEnumerable<SchemaContribution> contributions)
        {
            foreach (var contribution in contributions)
            {
                _RecordStep(dialect, contribution.Feature, StepOutcomeSkipped, contribution.Steps.Count);
            }
        }

        /// <summary>Records that one step of <paramref name="feature" /> was already recorded.</summary>
        public void StepSkipped(string dialect, string feature)
        {
            _RecordStep(dialect, feature, StepOutcomeSkipped, 1);
        }
#pragma warning restore CA1822

        /// <summary>Starts the wait for the per-database lock; dispose it without <see cref="LockWait.End" /> on failure.</summary>
        public LockWait StartLockWait(string dialect)
        {
            var span = SchemaRunnerDiagnostics.ActivitySource.StartActivity(
                LockWaitActivityName,
                ActivityKind.Internal
            );
            span?.SetTag(TagDialect, dialect);

            return new LockWait(span, dialect, timeProvider);
        }

        /// <summary>Starts one step; dispose it without <see cref="StepRun.Applied" /> on failure.</summary>
        public StepRun StartStep(string dialect, string feature, string version)
        {
            var span = SchemaRunnerDiagnostics.ActivitySource.StartActivity(StepActivityName, ActivityKind.Internal);

            if (span is not null)
            {
                span.SetTag(TagDialect, dialect);
                span.SetTag(TagFeature, _FeatureName(feature));
                span.SetTag(TagStepVersion, version);
            }

            return new StepRun(this, span, dialect, feature);
        }

        /// <summary>Records the concurrent-DDL races one database absorbed.</summary>
        public void RacesAbsorbed(string dialect, int races)
        {
            if (races <= 0)
            {
                return;
            }

            _absorbedRaces += races;

            if (_AbsorbedRaces.Enabled)
            {
                _AbsorbedRaces.Add(races, new TagList { { TagDialect, dialect } });
            }
        }

        /// <summary>Ends the pass span and records the pass duration.</summary>
        public void Complete(Exception? fault)
        {
            var outcome = fault is null ? OutcomeSuccess : OutcomeFailure;
            var tags = new TagList { { TagMode, mode }, { TagOutcome, outcome } };
            string? errorType = null;

            if (fault is not null)
            {
                errorType = _ErrorType(fault);
                tags.Add(TagErrorType, errorType);
            }

            if (_Duration.Enabled)
            {
                _Duration.Record(timeProvider.GetElapsedTime(_startedAt).TotalSeconds, tags);
            }

            if (activity is null)
            {
                return;
            }

            activity.SetTag(TagOutcome, outcome);
            activity.SetTag(TagStepsApplied, _stepsApplied);
            activity.SetTag(TagMismatchCount, _mismatches);
            activity.SetTag(TagAbsorbedRaces, _absorbedRaces);

            if (errorType is not null)
            {
                activity.SetTag(TagErrorType, errorType);
                // No description: a driver message can quote object names and connection details.
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }

        internal void StepRan(string dialect, string feature)
        {
            _stepsApplied++;
            _RecordStep(dialect, feature, StepOutcomeApplied, 1);
        }

        private static void _RecordStep(string dialect, string feature, string outcome, int count)
        {
            if (count <= 0 || !_Steps.Enabled)
            {
                return;
            }

            _Steps.Add(
                count,
                new TagList
                {
                    { TagDialect, dialect },
                    { TagFeature, _FeatureName(feature) },
                    { TagStepOutcome, outcome },
                }
            );
        }
    }

    /// <summary>One wait for the per-database lock.</summary>
    internal sealed class LockWait(Activity? span, string dialect, TimeProvider timeProvider) : IDisposable
    {
        private readonly long _startedAt = timeProvider.GetTimestamp();
        private bool _ended;

        /// <summary>Records how the wait ended: <see cref="LockAcquired" /> or <see cref="LockTimedOut" />.</summary>
        public void End(string outcome)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;

            if (_LockWaitDuration.Enabled)
            {
                _LockWaitDuration.Record(
                    timeProvider.GetElapsedTime(_startedAt).TotalSeconds,
                    new TagList { { TagDialect, dialect }, { TagLockOutcome, outcome } }
                );
            }

            if (span is null)
            {
                return;
            }

            span.SetTag(TagLockOutcome, outcome);

            if (!string.Equals(outcome, LockAcquired, StringComparison.Ordinal))
            {
                span.SetStatus(ActivityStatusCode.Error);
            }
        }

        /// <summary>Ends the span; a wait that never reported its end failed.</summary>
        public void Dispose()
        {
            End(LockFailed);
            span?.Dispose();
        }
    }

    /// <summary>One applied step: its DDL and its history row.</summary>
    internal sealed class StepRun(Pass pass, Activity? span, string dialect, string feature) : IDisposable
    {
        private bool _applied;

        /// <summary>Records that the step's DDL committed and its history row was written.</summary>
        public void Applied(int absorbedRaces)
        {
            _applied = true;
            pass.StepRan(dialect, feature);
            span?.SetTag(TagAbsorbedRaces, absorbedRaces);
        }

        /// <summary>Ends the span; a step that never reported success failed.</summary>
        public void Dispose()
        {
            if (!_applied)
            {
                span?.SetStatus(ActivityStatusCode.Error);
            }

            span?.Dispose();
        }
    }
}
