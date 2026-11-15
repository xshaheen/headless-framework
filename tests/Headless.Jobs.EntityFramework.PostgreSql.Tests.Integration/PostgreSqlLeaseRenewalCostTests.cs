// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Tests;

/// <summary>Runs the lease-renewal cost suite against PostgreSQL, counted from Npgsql's command tracing.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlLeaseRenewalCostTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsLeaseRenewalCostConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    protected override JobsRoundTripMeter CreateMeter() => new NpgsqlCommandMeter();

    [Fact]
    public override Task a_running_time_job_renews_its_lease_without_a_probe_or_a_transaction()
    {
        return base.a_running_time_job_renews_its_lease_without_a_probe_or_a_transaction();
    }

    [Fact]
    public override Task a_running_cron_occurrence_renews_its_lease_in_one_statement_without_a_transaction()
    {
        return base.a_running_cron_occurrence_renews_its_lease_in_one_statement_without_a_transaction();
    }

    /// <summary>
    /// Counts the commands Npgsql traces in the measured call's own async flow, so tests running in parallel add
    /// nothing. Npgsql traces no transaction: BEGIN rides on the next command and COMMIT is untraced, so round trips
    /// come from the SQL Server run of the same code path.
    /// </summary>
    private sealed class NpgsqlCommandMeter : JobsRoundTripMeter
    {
        private static readonly AsyncLocal<NpgsqlCommandMeter?> _Measuring = new();
        private readonly ActivityListener _listener;
        private long _commands;

        public NpgsqlCommandMeter()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static source => string.Equals(source.Name, "Npgsql", StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = static activity =>
                {
                    if (_Measuring.Value is { } meter)
                    {
                        Interlocked.Increment(ref meter._commands);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public override async Task<JobsStoreCost> MeasureAsync(Func<Task<int>> call)
        {
            Interlocked.Exchange(ref _commands, 0);
            _Measuring.Value = this;

            try
            {
                var result = await call();

                return new JobsStoreCost(result, Interlocked.Read(ref _commands), RoundTrips: null);
            }
            finally
            {
                _Measuring.Value = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _listener.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
