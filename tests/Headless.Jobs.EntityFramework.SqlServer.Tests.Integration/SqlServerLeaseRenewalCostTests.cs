// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

/// <summary>Runs the lease-renewal cost suite against SQL Server, counted by SqlClient's own connection statistics.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerLeaseRenewalCostTests(SqlServerJobsCoordinationFixture fixture)
    : JobsLeaseRenewalCostConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    protected override JobsRoundTripMeter CreateMeter() => new SqlClientRoundTripMeter();

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
    /// Turns statistics on for every connection the Jobs context creates. They count each executed batch and each server
    /// round trip, BEGIN and COMMIT included, so a transaction around a statement shows as round trips beyond its
    /// commands.
    /// </summary>
    private sealed class SqlClientRoundTripMeter : JobsRoundTripMeter
    {
        private readonly ConcurrentQueue<SqlConnection> _connections = new();

        public override DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, DbConnection result)
        {
            if (result is SqlConnection connection)
            {
                connection.StatisticsEnabled = true;
                _connections.Enqueue(connection);
            }

            return result;
        }

        public override async Task<JobsStoreCost> MeasureAsync(Func<Task<int>> call)
        {
            var (executionsBefore, roundTripsBefore) = _Totals();
            var result = await call();
            var (executionsAfter, roundTripsAfter) = _Totals();
            _connections.Should().NotBeEmpty("the meter must see the connections the Jobs context creates");

            return new JobsStoreCost(result, executionsAfter - executionsBefore, roundTripsAfter - roundTripsBefore);
        }

        private (long Executions, long RoundTrips) _Totals()
        {
            long executions = 0,
                roundTrips = 0;

            foreach (var connection in _connections)
            {
                IDictionary statistics = connection.RetrieveStatistics();
                executions += _Read(statistics, "PreparedExecs") + _Read(statistics, "UnpreparedExecs");
                roundTrips += _Read(statistics, "ServerRoundtrips");
            }

            return (executions, roundTrips);
        }

        private static long _Read(IDictionary statistics, string name) =>
            statistics[name] is long value
                ? value
                : throw new InvalidOperationException($"SqlClient statistics carry no '{name}'.");
    }
}
