// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Jobs;

namespace Tests.Cluster;

/// <summary>
/// Reads one run's time-job rows straight from its database, on the test's own connection, so what it reports does not
/// depend on any node being reachable.
/// </summary>
public sealed class ClusterStoreReader(ClusterStoreFixture store, string connectionString)
{
    private int _observationFailures;

    /// <summary>The harness view of this run's database.</summary>
    public IJobsCoordinationFixture View { get; } = store.View(connectionString);

    /// <summary>How many row observations by token-ignoring bodies failed to read the store.</summary>
    public int CountObservationFailures() => Volatile.Read(ref _observationFailures);

    /// <summary>Every time-job row.</summary>
    public async Task<IReadOnlyList<StoreRow>> ReadRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = store.CreateConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = View.Sql(
            "SELECT \"Id\", \"Status\", \"LeaseGeneration\", \"LeaseState\", \"LeaseExpiresAt\", \"LeaseHolder\", "
                + $"\"LeaseTakeoverCount\", \"RetryCount\" FROM {View.QualifiedTimeJobsTable};"
        );

        var rows = new List<StoreRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(
                new StoreRow(
                    reader.GetGuid(0),
                    Enum.Parse<JobStatus>(reader.GetString(1)),
                    await _ReadLeaseAsync(reader, 2, cancellationToken).ConfigureAwait(false),
                    Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture)
                )
            );
        }

        return rows;
    }

    /// <summary>
    /// What a body sees of its own row just before it returns: the lease, and whether the lease deadline has passed on
    /// the store's clock, the clock the completion fence judges it on. Null when the read itself failed.
    /// </summary>
    public async Task<RowObservation?> ObserveAsync(Guid id)
    {
        try
        {
            await using var connection = store.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = View.Sql(
                "SELECT \"Status\", \"LeaseGeneration\", \"LeaseState\", \"LeaseHolder\", "
                    + $"CASE WHEN \"LeaseExpiresAt\" IS NOT NULL AND \"LeaseExpiresAt\" < {View.LeaseDatabaseClockSql} THEN 1 ELSE 0 END "
                    + $"FROM {View.QualifiedTimeJobsTable} WHERE \"Id\" = @id;"
            );
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = id;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            if (!await reader.ReadAsync().ConfigureAwait(false))
            {
                return null;
            }

            return new RowObservation(
                Enum.Parse<JobStatus>(reader.GetString(0)),
                await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : reader.GetString(3),
                Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                (RowLeaseState)Convert.ToInt16(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture) == 1
            );
        }
        catch (DbException)
        {
            // The observation only sharpens the stale-settle check; a failed read is counted and reported instead.
            Interlocked.Increment(ref _observationFailures);
            return null;
        }
    }

    private static async Task<RowLeaseSnapshot> _ReadLeaseAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        var generation = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        var state = (RowLeaseState)Convert.ToInt16(reader.GetValue(ordinal + 1), CultureInfo.InvariantCulture);
        var expiresAt = await reader.IsDBNullAsync(ordinal + 2, cancellationToken).ConfigureAwait(false)
            ? (DateTimeOffset?)null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal + 2, cancellationToken).ConfigureAwait(false);
        var holder = await reader.IsDBNullAsync(ordinal + 3, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal + 3);
        var takeovers = Convert.ToInt32(reader.GetValue(ordinal + 4), CultureInfo.InvariantCulture);

        return new RowLeaseSnapshot(generation, state, expiresAt, holder, takeovers);
    }
}
