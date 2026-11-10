// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Hosting;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.PostgreSql;

/// <summary>
/// The PostgreSQL <see cref="IAuditLogPartitions{TContext}"/>: calls the partition functions the schema step created.
/// They run with their owner's rights, so the provider's login needs only <c>EXECUTE</c> on them.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class PostgreSqlAuditLogPartitions<TContext>(
    RelationalAuditLogTable table,
    IOptions<AuditLogStorageOptions> storageOptionsAccessor,
    TimeProvider timeProvider
) : IAuditLogPartitions<TContext>
{
    private readonly AuditLogStorageOptions _storageOptions = storageOptionsAccessor.Value;

    public Task<int> EnsureAsync(CancellationToken cancellationToken = default)
    {
        _EnsurePartitioned();

        return PostgreSqlAuditLogPartitionCommands.EnsureAsync(
            table,
            timeProvider.GetUtcNow(),
            _storageOptions.PartitionMonthsAhead,
            cancellationToken
        );
    }

    public async Task<int> DropExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        Argument.IsPositive(olderThan);
        _EnsurePartitioned();

        var function = PostgreSqlAuditLogSql.Qualify(
            table.Schema,
            PostgreSqlAuditLogSql.DropPartitionsFunction(table.Name)
        );

        await using var connection = table.Dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = table.Options.CommandTimeoutSeconds;
        command.CommandText = $"SELECT {function}(@Cutoff);";
        table.Dialect.AddParameter(command, "Cutoff", SqlColumnType.Timestamp, timeProvider.GetUtcNow() - olderThan);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    private void _EnsurePartitioned()
    {
        if (_storageOptions.Partitioning is not AuditLogPartitioning.Monthly)
        {
            throw new InvalidOperationException(
                $"The audit log table is not partitioned. Set {nameof(AuditLogStorageOptions)}.{nameof(AuditLogStorageOptions.Partitioning)} to {nameof(AuditLogPartitioning.Monthly)} before the table is created."
            );
        }
    }
}

/// <summary>The partition statements the runtime service and the provisioner share.</summary>
internal static class PostgreSqlAuditLogPartitionCommands
{
    public static async Task<int> EnsureAsync(
        RelationalAuditLogTable table,
        DateTimeOffset from,
        int monthsAhead,
        CancellationToken cancellationToken
    )
    {
        var function = PostgreSqlAuditLogSql.Qualify(
            table.Schema,
            PostgreSqlAuditLogSql.EnsurePartitionsFunction(table.Name)
        );

        await using var connection = table.Dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = table.Options.CommandTimeoutSeconds;
        command.CommandText = $"SELECT {function}(@From, @MonthsAhead);";
        table.Dialect.AddParameter(command, "From", SqlColumnType.Timestamp, from);
        table.Dialect.AddParameter(command, "MonthsAhead", SqlColumnType.Int32, monthsAhead);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>Returns the names of the table's partitions, oldest name first.</summary>
    public static async Task<IReadOnlyList<string>> ListAsync(
        RelationalAuditLogTable table,
        CancellationToken cancellationToken
    )
    {
        await using var connection = table.Dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = table.Options.CommandTimeoutSeconds;
        command.CommandText = """
            SELECT c.relname
            FROM pg_catalog.pg_inherits AS i
            JOIN pg_catalog.pg_class AS c ON c.oid = i.inhrelid
            WHERE i.inhparent = pg_catalog.to_regclass(@Table)
            ORDER BY c.relname;
            """;
        table.Dialect.AddParameter(command, "Table", SqlColumnType.Text(-1), table.Qualified);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
#pragma warning restore CA2100

/// <summary>
/// Offers the audit log's partitions as a provisioned resource: <c>resources provision</c> creates the current month
/// and the configured months ahead, on every release, and a starting host only checks that the current month has one.
/// </summary>
/// <remarks>
/// The partitions are not a schema step because they depend on the date and on
/// <see cref="AuditLogStorageOptions.PartitionMonthsAhead"/>, which an operator may change: a step's SQL is pinned by
/// its checksum. Purge and delete are left to the audit log's schema resource, which owns the table.
/// </remarks>
internal sealed class PostgreSqlAuditLogPartitionsProvisioner(
    RelationalAuditLogTable table,
    AuditLogStorageOptions storageOptions,
    TimeProvider timeProvider
) : IResourceProvisioner
{
    public const string ResourceKind = "AuditLogPartitions";

    public string Kind => ResourceKind;

    public string Name => $"{table.Schema}/{table.Name}";

    public bool CanPurge => false;

    public bool CanDelete => false;

    public Task ProvisionAsync(CancellationToken cancellationToken)
    {
        return PostgreSqlAuditLogPartitionCommands.EnsureAsync(
            table,
            timeProvider.GetUtcNow(),
            storageOptions.PartitionMonthsAhead,
            cancellationToken
        );
    }

    // One catalog read. Without the current month's partition every audited write fails.
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var current = _PartitionName(timeProvider.GetUtcNow());
        var partitions = await PostgreSqlAuditLogPartitionCommands
            .ListAsync(table, cancellationToken)
            .ConfigureAwait(false);

        if (!partitions.Contains(current, StringComparer.Ordinal))
        {
            throw new ResourceProvisioningException(
                $"The audit log table {table.Schema}.{table.Name} has no partition for the current month ({current}), so "
                    + $"audit entries cannot be written. Run 'resources provision -k {ResourceKind}' to create it."
            );
        }
    }

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Audit log partitions are purged with the audit log's schema resource.");

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Audit log partitions are deleted with the audit log's schema resource.");

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var partitions = await PostgreSqlAuditLogPartitionCommands
            .ListAsync(table, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            new("partitions", partitions.Count.ToString(CultureInfo.InvariantCulture)),
            new("first", partitions.Count == 0 ? "none" : partitions[0]),
            new("last", partitions.Count == 0 ? "none" : partitions[^1]),
        ];
    }

    // The functions and the partitioned table come from the audit log's schema steps.
    public IEnumerable<IResourceProvisioner> DependsOn(IReadOnlyList<IResourceProvisioner> provisioners) =>
        provisioners.Where(static p => string.Equals(p.Kind, "Schema", StringComparison.Ordinal));

    // Must match the name the ensure function builds: '{table}_' || to_char(month, 'YYYY_MM'), in UTC.
    private string _PartitionName(DateTimeOffset instant)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{table.Name}_{instant.UtcDateTime:yyyy_MM}");
    }
}

/// <summary>Provides the partitions resource only when the table is partitioned, which the options decide.</summary>
internal sealed class PostgreSqlAuditLogPartitionsProvisionerSource(IServiceProvider services)
    : IResourceProvisionerSource
{
    public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken)
    {
        var storageOptions = services.GetRequiredService<IOptions<AuditLogStorageOptions>>().Value;

        IReadOnlyList<IResourceProvisioner> provisioners =
            storageOptions.Partitioning is AuditLogPartitioning.Monthly
                ?
                [
                    new PostgreSqlAuditLogPartitionsProvisioner(
                        services.GetRequiredService<RelationalAuditLogTable>(),
                        storageOptions,
                        services.GetRequiredService<TimeProvider>()
                    ),
                ]
                : [];

        return Task.FromResult(provisioners);
    }
}
