// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using System.Text;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// Applies, verifies, and exports the schema steps every registered feature contributes. Contributions that reach
/// the same database are applied in one pass under one session lock, and each applied step is recorded with a
/// checksum in a <c>headless_schema_history</c> table in its schema.
/// </summary>
/// <remarks>
/// <para>
/// One lock per database replaces the per-feature locks of the hand-written initializers. Those let two features race
/// the shared <c>CREATE SCHEMA</c>, and on PostgreSQL the loser's rollback silently discarded its DDL. Under one lock,
/// replicas of one deployment never race each other at all.
/// </para>
/// <para>
/// A creator outside the lock, such as a consumer's EF migration, can still commit an object first. The runner then
/// follows the rule every initializer followed: a step that fails with the dialect's already-created error is re-run
/// once in a fresh transaction, and a second failure propagates. The re-run is safe because steps are idempotent.
/// </para>
/// <para>
/// A step is recorded in its own transaction after its DDL commits, so a history row exists only for committed DDL.
/// A crash between the two leaves the objects without a row, and the next run re-applies the idempotent step.
/// </para>
/// </remarks>
/// <param name="contributions">Every feature's contribution.</param>
/// <param name="logger">Logs applied steps, absorbed races, and lock release failures.</param>
/// <param name="timeProvider">Clock for the lock-wait bound and poll delay. Defaults to the system clock.</param>
/// <param name="lockTimeout">How long to wait for another runner's lock. Defaults to <see cref="DefaultLockTimeout"/>.</param>
/// <param name="commandTimeout">Timeout of every statement the runner sends. Defaults to <see cref="DefaultCommandTimeout"/>.</param>
/// <exception cref="ArgumentNullException"><paramref name="contributions"/> is null.</exception>
#pragma warning disable CA2100 // SQL text comes from dialect code and contributions, never from request input.
[PublicAPI]
public sealed partial class SchemaRunner(
    IEnumerable<SchemaContribution> contributions,
    ILogger<SchemaRunner>? logger = null,
    TimeProvider? timeProvider = null,
    TimeSpan? lockTimeout = null,
    TimeSpan? commandTimeout = null
)
{
    /// <summary>The history table's name in every schema the runner manages.</summary>
    public const string HistoryTableName = "headless_schema_history";

    /// <summary>The default bound on how long a runner waits for another runner's lock.</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The default timeout of every statement the runner sends. Sized for DDL, not OLTP: an index build on a table
    /// that already holds rows can run for minutes, and a timeout mid-build fails startup.
    /// </summary>
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan _LockPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    private readonly int _commandTimeoutSeconds = (int)
        Math.Ceiling((commandTimeout ?? DefaultCommandTimeout).TotalSeconds);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>Every registered contribution, in registration order.</summary>
    public IReadOnlyList<SchemaContribution> Contributions { get; } = [.. Argument.IsNotNull(contributions)];

    /// <summary>
    /// Applies every missing step of every contribution whose <see cref="SchemaContribution.ApplyOnStartup"/> is set.
    /// Per database: one connection and one history read per schema; only when a step is missing does it take the
    /// lock, re-read, and run the missing steps' DDL. A warm database therefore costs no lock and no DDL, whatever the
    /// number of features.
    /// </summary>
    /// <param name="cancellationToken">Cancels the run. The lock is still released.</param>
    /// <returns>The applied steps, history mismatches, and absorbed races.</returns>
    /// <exception cref="SchemaRunnerException">
    /// A connection could not be opened, the lock was not acquired within the timeout, or a step failed on its second
    /// attempt. The driver's exception is the inner exception.
    /// </exception>
    public Task<SchemaRunnerResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        return SchemaRunnerTelemetry.ObserveAsync(
            SchemaRunnerMode.Apply,
            _timeProvider,
            pass => _ApplyAsync(pass, cancellationToken)
        );
    }

    private async Task<SchemaRunnerResult> _ApplyAsync(
        SchemaRunnerTelemetry.Pass? pass,
        CancellationToken cancellationToken
    )
    {
        var applied = new List<SchemaAppliedStep>();
        var mismatches = new List<SchemaMismatch>();
        var races = 0;

        foreach (var group in await _GroupByDatabaseAsync(applyingOnly: true).ConfigureAwait(false))
        {
            races += await _ApplyGroupAsync(group, applied, mismatches, pass, cancellationToken).ConfigureAwait(false);
        }

        return new SchemaRunnerResult
        {
            AppliedSteps = applied,
            Mismatches = mismatches,
            AbsorbedRaces = races,
        };
    }

    /// <summary>
    /// Applies (<see cref="SchemaRunnerMode.Apply"/>) or verifies (<see cref="SchemaRunnerMode.Verify"/>) the
    /// registered contributions, and throws when the history disagrees in a way the mode cannot accept: a changed
    /// checksum in either mode, and a missing step in verify mode. <see cref="SchemaMismatchKind.Unknown"/> rows are
    /// returned, never thrown: an older replica starting after a newer one applied a step is a normal rolling deploy.
    /// </summary>
    /// <param name="mode">Whether to apply missing steps or only compare.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>Every mismatch found, including the tolerated ones.</returns>
    /// <exception cref="SchemaRunnerException">The run failed, or the history disagrees with the registered steps.</exception>
    public Task<IReadOnlyList<SchemaMismatch>> RunAsync(
        SchemaRunnerMode mode,
        CancellationToken cancellationToken = default
    )
    {
        // One pass span covers the fatal-mismatch check too, so a startup that fails on history reads as a failed pass.
        return SchemaRunnerTelemetry.ObserveAsync(
            mode,
            _timeProvider,
            pass => _RunAsync(mode, pass, cancellationToken)
        );
    }

    private async Task<IReadOnlyList<SchemaMismatch>> _RunAsync(
        SchemaRunnerMode mode,
        SchemaRunnerTelemetry.Pass? pass,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<SchemaMismatch> mismatches;
        SchemaMismatchKind[] fatal;

        if (mode == SchemaRunnerMode.Verify)
        {
            mismatches = await _VerifyAsync(pass, cancellationToken).ConfigureAwait(false);
            fatal = [SchemaMismatchKind.Missing, SchemaMismatchKind.Checksum];
        }
        else
        {
            mismatches = (await _ApplyAsync(pass, cancellationToken).ConfigureAwait(false)).Mismatches;
            fatal = [SchemaMismatchKind.Checksum];
        }

        var failures = mismatches.Where(m => fatal.Contains(m.Kind)).ToList();

        if (failures.Count > 0)
        {
            throw new SchemaRunnerException(
                $"Headless schema runner ({mode} mode): the database history disagrees with the registered steps: "
                    + $"{string.Join("; ", failures)}. A missing step needs the exported deploy script or Apply mode; a "
                    + "changed checksum means a released step was edited, so add a new step instead."
            )
            {
                Mismatches = failures,
            };
        }

        return mismatches;
    }

    /// <summary>
    /// Compares every registered contribution with its schema's history without writing. A missing history table
    /// reports every step as <see cref="SchemaMismatchKind.Missing"/>. Rows of features this host does not register
    /// are ignored, so hosts with different feature sets can share one schema.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every mismatch; empty when the database holds exactly what the code expects.</returns>
    /// <exception cref="SchemaRunnerException">A connection could not be opened or the history could not be read.</exception>
    public Task<IReadOnlyList<SchemaMismatch>> VerifyAsync(CancellationToken cancellationToken = default)
    {
        return SchemaRunnerTelemetry.ObserveAsync(
            SchemaRunnerMode.Verify,
            _timeProvider,
            pass => _VerifyAsync(pass, cancellationToken)
        );
    }

    private async Task<IReadOnlyList<SchemaMismatch>> _VerifyAsync(
        SchemaRunnerTelemetry.Pass? pass,
        CancellationToken cancellationToken
    )
    {
        var mismatches = new List<SchemaMismatch>();

        foreach (var group in await _GroupByDatabaseAsync(applyingOnly: false).ConfigureAwait(false))
        {
            await using var connection = await _OpenAsync(group, cancellationToken).ConfigureAwait(false);

            foreach (var schema in group.Schemas)
            {
                var history = await _ReadHistoryAsync(connection, group.Dialect, schema, cancellationToken)
                    .ConfigureAwait(false);
                var found = _Compare(schema, group.InSchema(schema), history);
                pass?.MismatchesFound(group.Dialect.Name, found);
                mismatches.AddRange(found);
            }
        }

        return mismatches;
    }

    /// <summary>
    /// Renders the reviewable deploy script for <paramref name="dialect"/>: per schema, the history table, then every
    /// step followed by its history insert. Deterministic for a given set of contributions, so CI can commit it and
    /// diff it. Applied with the plain database client, it leaves a database that <see cref="VerifyAsync"/> accepts,
    /// and running it twice changes nothing.
    /// </summary>
    /// <param name="dialect">The dialect to export; contributions of other dialects are skipped.</param>
    /// <returns>The script text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is null.</exception>
    public string ExportScript(ISchemaDialect dialect)
    {
        Argument.IsNotNull(dialect);

        var script = new StringBuilder();
        script.AppendLine("-- Headless schema runner deploy script");
        script.Append("-- Dialect: ").AppendLine(dialect.Name);
        script.AppendLine("-- Idempotent: every statement is guarded, so running it twice changes nothing.");

        var ofDialect = Contributions.Where(c => string.Equals(c.Dialect.Name, dialect.Name, StringComparison.Ordinal));

        foreach (var schemaGroup in ofDialect.GroupBy(c => c.Schema, StringComparer.Ordinal))
        {
            var schema = schemaGroup.Key;
            script.AppendLine();
            script.Append("-- Schema ").AppendLine(schema);
            _AppendBatch(script, dialect, dialect.HistoryTableSql(schema));

            foreach (var contribution in _Distinct(schemaGroup))
            {
                foreach (var step in contribution.Steps)
                {
                    script.AppendLine();
                    script
                        .Append("-- ")
                        .Append(contribution.Feature)
                        .Append('/')
                        .Append(step.Version)
                        .Append(": ")
                        .AppendLine(step.Description);
                    _AppendBatch(script, dialect, step.Sql);
                    _AppendBatch(script, dialect, _InlineHistoryInsert(dialect, schema, contribution.Feature, step));
                }
            }
        }

        return script.ToString();
    }

    private async Task<int> _ApplyGroupAsync(
        DatabaseGroup group,
        List<SchemaAppliedStep> applied,
        List<SchemaMismatch> mismatches,
        SchemaRunnerTelemetry.Pass? pass,
        CancellationToken cancellationToken
    )
    {
        var dialect = group.Dialect;
        var races = 0;

        await using var connection = await _OpenAsync(group, cancellationToken).ConfigureAwait(false);

        // Warm path: when every step is already recorded there is no DDL to serialize, so the lock, the history DDL,
        // and the re-read are skipped. A warm start then costs one query per schema whatever the feature count. A
        // step recorded by a concurrent runner after this read only sends us down the locked path, which re-reads.
        var warm = await _AllRecordedAsync(group, connection, mismatches, pass, cancellationToken)
            .ConfigureAwait(false);

        if (warm)
        {
            pass?.StepsSkipped(dialect.Name, group.Schemas.SelectMany(group.InSchema));

            return 0;
        }

        // Keyed on the database, not a feature or schema, so every runner that reaches this database serializes
        // behind one lock, whichever features it registers.
        var lockResource = $"headless_schema_runner:{group.Identity}";

        // Scoped to the wait alone, so the steps that follow are not children of the lock-wait span.
        using (var lockWait = pass?.StartLockWait(dialect.Name))
        {
            await _AcquireLockAsync(connection, dialect, lockResource, lockWait, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            foreach (var schema in group.Schemas)
            {
                races += await _RunStepAsync(
                        connection,
                        dialect,
                        dialect.HistoryTableSql(schema),
                        $"{schema}/{HistoryTableName}",
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                var inSchema = group.InSchema(schema);
                var history = await _ReadHistoryAsync(connection, dialect, schema, cancellationToken)
                    .ConfigureAwait(false);

                // Missing is not a mismatch here: applying it is the point of the run.
                var found = _Compare(schema, inSchema, history)
                    .Where(m => m.Kind != SchemaMismatchKind.Missing)
                    .ToList();
                pass?.MismatchesFound(dialect.Name, found);
                mismatches.AddRange(found);

                var recorded = history.Select(h => (h.Feature, h.Version)).ToHashSet();

                foreach (var contribution in inSchema)
                {
                    foreach (var step in contribution.Steps)
                    {
                        // A recorded step is never re-run, even when its checksum changed: re-running edited DDL
                        // against objects created by the old DDL is the drift the checksum exists to stop.
                        if (!recorded.Add((contribution.Feature, step.Version)))
                        {
                            pass?.StepSkipped(dialect.Name, contribution.Feature);

                            continue;
                        }

#pragma warning disable CA2000 // False positive: the using statement disposes it on every path; the analyzer loses track inside an awaited loop.
                        using (var stepRun = pass?.StartStep(dialect.Name, contribution.Feature, step.Version))
#pragma warning restore CA2000
                        {
                            var stepRaces = await _RunStepAsync(
                                    connection,
                                    dialect,
                                    step.Sql,
                                    $"{schema}/{contribution.Feature}/{step.Version}",
                                    cancellationToken
                                )
                                .ConfigureAwait(false);
                            races += stepRaces;

                            await _RecordAsync(
                                    connection,
                                    dialect,
                                    schema,
                                    contribution.Feature,
                                    step,
                                    cancellationToken
                                )
                                .ConfigureAwait(false);

                            stepRun?.Applied(stepRaces);
                            applied.Add(new SchemaAppliedStep(schema, contribution.Feature, step.Version));
                        }

                        LogStepApplied(_logger, dialect.Name, schema, contribution.Feature, step.Version);
                    }
                }
            }
        }
        finally
        {
            await _ReleaseLockAsync(connection, dialect, lockResource).ConfigureAwait(false);
        }

        pass?.RacesAbsorbed(dialect.Name, races);

        return races;
    }

    private async Task<bool> _AllRecordedAsync(
        DatabaseGroup group,
        DbConnection connection,
        List<SchemaMismatch> mismatches,
        SchemaRunnerTelemetry.Pass? pass,
        CancellationToken cancellationToken
    )
    {
        var found = new List<SchemaMismatch>();

        foreach (var schema in group.Schemas)
        {
            var history = await _ReadHistoryAsync(connection, group.Dialect, schema, cancellationToken)
                .ConfigureAwait(false);
            found.AddRange(_Compare(schema, group.InSchema(schema), history));

            if (found.Exists(m => m.Kind == SchemaMismatchKind.Missing))
            {
                return false;
            }
        }

        pass?.MismatchesFound(group.Dialect.Name, found);
        mismatches.AddRange(found);

        return true;
    }

    private async Task _AcquireLockAsync(
        DbConnection connection,
        ISchemaDialect dialect,
        string lockResource,
        SchemaRunnerTelemetry.LockWait? lockWait,
        CancellationToken cancellationToken
    )
    {
        var started = _timeProvider.GetTimestamp();

        while (true)
        {
            await using var command = _CreateCommand(connection);
            command.CommandText = dialect.TryAcquireLockSql;
            command.Parameters.Add(dialect.CreateStringParameter("LockResource", lockResource));

            var acquired = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (Convert.ToBoolean(acquired, CultureInfo.InvariantCulture))
            {
                lockWait?.End(SchemaRunnerTelemetry.LockAcquired);

                return;
            }

            if (_timeProvider.GetElapsedTime(started) >= _lockTimeout)
            {
                lockWait?.End(SchemaRunnerTelemetry.LockTimedOut);

                throw new SchemaRunnerException(
                    $"Headless schema runner: timed out after {_lockTimeout} waiting for the schema lock "
                        + $"'{lockResource}'. Another runner is applying steps to this database, or a crashed one "
                        + "still holds the session."
                );
            }

            // Between polls this connection runs no statement and holds no snapshot, so a lock holder running
            // CREATE INDEX CONCURRENTLY never waits on it.
            await Task.Delay(_LockPollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _ReleaseLockAsync(DbConnection connection, ISchemaDialect dialect, string lockResource)
    {
        try
        {
            await using var command = _CreateCommand(connection);
            command.CommandText = dialect.ReleaseLockSql;
            command.Parameters.Add(dialect.CreateStringParameter("LockResource", lockResource));
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // The lock is session-scoped, so disposing the connection releases it; a failed release must not replace
            // the exception that brought the run here.
            LogLockReleaseFailed(_logger, lockResource, ex);
        }
    }

    private async Task<int> _RunStepAsync(
        DbConnection connection,
        ISchemaDialect dialect,
        string sql,
        string stepName,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using var command = _CreateCommand(connection);
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return attempt - 1;
            }
            catch (Exception ex) when (attempt == 1 && dialect.IsAlreadyCreatedRace(ex))
            {
                // The conflicting creator has committed by the time the error surfaces, so the re-run's guards see
                // its objects and create only what this transaction's rollback discarded.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                LogRaceAbsorbed(_logger, dialect.Name, stepName, ex.Message);
            }
            catch (Exception ex) when (ex is DbException)
            {
                throw new SchemaRunnerException(
                    $"Headless schema runner: step {stepName} failed on {dialect.Name}.",
                    ex
                );
            }
        }
    }

    private async Task _RecordAsync(
        DbConnection connection,
        ISchemaDialect dialect,
        string schema,
        string feature,
        SchemaStep step,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var command = _CreateCommand(connection);
            command.CommandText = dialect.InsertHistorySql(schema);
            command.Parameters.Add(dialect.CreateStringParameter("Feature", feature));
            command.Parameters.Add(dialect.CreateStringParameter("StepVersion", step.Version));
            command.Parameters.Add(dialect.CreateStringParameter("Description", step.Description));
            command.Parameters.Add(dialect.CreateStringParameter("Checksum", SchemaRunnerChecksum.ForStep(step)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            throw new SchemaRunnerException(
                $"Headless schema runner: step {schema}/{feature}/{step.Version} committed but its history row was "
                    + "not recorded; the next run re-applies the idempotent step.",
                ex
            );
        }
    }

    private async Task<List<HistoryRow>> _ReadHistoryAsync(
        DbConnection connection,
        ISchemaDialect dialect,
        string schema,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<HistoryRow>();

        try
        {
            await using var command = _CreateCommand(connection);
            command.CommandText = dialect.ReadHistorySql(schema);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new HistoryRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }
        catch (Exception ex) when (dialect.IsObjectNotFound(ex))
        {
            // No history table: nothing was applied here, and the comparison reports every step missing.
            LogHistoryAbsent(_logger, dialect.Name, schema);
        }
        catch (DbException ex)
        {
            throw new SchemaRunnerException(
                $"Headless schema runner: failed to read the history of schema {schema} on {dialect.Name}.",
                ex
            );
        }

        return rows;
    }

    private static List<SchemaMismatch> _Compare(
        string schema,
        IReadOnlyList<SchemaContribution> contributions,
        List<HistoryRow> history
    )
    {
        var registered = new Dictionary<(string Feature, string Version), string>();

        foreach (var contribution in contributions)
        {
            foreach (var step in contribution.Steps)
            {
                registered[(contribution.Feature, step.Version)] = SchemaRunnerChecksum.ForStep(step);
            }
        }

        var features = contributions.Select(c => c.Feature).ToHashSet(StringComparer.Ordinal);
        var mismatches = new List<SchemaMismatch>();

        // Rows of features this host does not register belong to another host sharing the schema, not to drift.
        foreach (var row in history.Where(h => features.Contains(h.Feature)))
        {
            if (!registered.TryGetValue((row.Feature, row.Version), out var expected))
            {
                mismatches.Add(new(schema, row.Feature, row.Version, SchemaMismatchKind.Unknown, null, row.Checksum));
            }
            else if (!string.Equals(expected, row.Checksum, StringComparison.Ordinal))
            {
                mismatches.Add(
                    new(schema, row.Feature, row.Version, SchemaMismatchKind.Checksum, expected, row.Checksum)
                );
            }
        }

        var recorded = history.Select(h => (h.Feature, h.Version)).ToHashSet();

        foreach (var ((feature, version), expected) in registered)
        {
            if (!recorded.Contains((feature, version)))
            {
                mismatches.Add(new(schema, feature, version, SchemaMismatchKind.Missing, expected, null));
            }
        }

        return mismatches;
    }

    private static async Task<DbConnection> _OpenAsync(DatabaseGroup group, CancellationToken cancellationToken)
    {
        // Microsoft.Data.Sqlite completes every call synchronously, waiting out another writer's lock on the calling
        // thread; yielding first hands the caller a pending task instead of blocking it through that wait.
        await Task.Yield();
        var connection = group.Contributions[0].CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch (DbException ex)
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw new SchemaRunnerException(
                $"Headless schema runner: failed to open the {group.Dialect.Name} connection for "
                    + $"{string.Join(", ", group.Contributions.Select(c => c.Feature).Distinct(StringComparer.Ordinal))}.",
                ex
            );
        }
    }

    private async Task<List<DatabaseGroup>> _GroupByDatabaseAsync(bool applyingOnly)
    {
        var groups = new List<DatabaseGroup>();

        foreach (var contribution in Contributions.Where(c => !applyingOnly || c.ApplyOnStartup))
        {
            // Identity comes from the unopened connection's settings, so features that each build their own
            // connection to one database still share one group, one lock, and one pass.
            string identity;

            await using (var probe = contribution.CreateConnection())
            {
                identity = contribution.Dialect.DatabaseIdentity(probe);
            }

            var group = groups.Find(g =>
                string.Equals(g.Dialect.Name, contribution.Dialect.Name, StringComparison.Ordinal)
                && string.Equals(g.Identity, identity, StringComparison.Ordinal)
            );

            if (group is null)
            {
                group = new DatabaseGroup(contribution.Dialect, identity);
                groups.Add(group);
            }

            group.Contributions.Add(contribution);
        }

        return groups;
    }

    private static IEnumerable<SchemaContribution> _Distinct(IEnumerable<SchemaContribution> contributions)
    {
        // A feature registered twice for one schema contributes its steps once.
        return contributions.DistinctBy(c => c.Feature, StringComparer.Ordinal);
    }

    private static string _InlineHistoryInsert(ISchemaDialect dialect, string schema, string feature, SchemaStep step)
    {
        static string literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        return dialect
            .InsertHistorySql(schema)
            .Replace("@Feature", literal(feature), StringComparison.Ordinal)
            .Replace("@StepVersion", literal(step.Version), StringComparison.Ordinal)
            .Replace("@Description", literal(step.Description), StringComparison.Ordinal)
            .Replace("@Checksum", literal(SchemaRunnerChecksum.ForStep(step)), StringComparison.Ordinal);
    }

    private static void _AppendBatch(StringBuilder script, ISchemaDialect dialect, string sql)
    {
        script.AppendLine(sql.TrimEnd());

        if (dialect.ScriptBatchSeparator is { } separator)
        {
            script.AppendLine(separator);
        }
    }

    private DbCommand _CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandTimeout = _commandTimeoutSeconds;

        return command;
    }

    private sealed record HistoryRow(string Feature, string Version, string Checksum);

    private sealed class DatabaseGroup(ISchemaDialect dialect, string identity)
    {
        public ISchemaDialect Dialect { get; } = dialect;

        public string Identity { get; } = identity;

        public List<SchemaContribution> Contributions { get; } = [];

        public IEnumerable<string> Schemas => Contributions.Select(c => c.Schema).Distinct(StringComparer.Ordinal);

        public IReadOnlyList<SchemaContribution> InSchema(string schema)
        {
            return [.. _Distinct(Contributions.Where(c => string.Equals(c.Schema, schema, StringComparison.Ordinal)))];
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "SchemaRunnerStepApplied",
        Level = LogLevel.Information,
        Message = "Schema runner applied {Dialect} step {Schema}/{Feature}/{Version}."
    )]
    private static partial void LogStepApplied(
        ILogger logger,
        string dialect,
        string schema,
        string feature,
        string version
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SchemaRunnerRaceAbsorbed",
        Level = LogLevel.Information,
        Message = "Schema runner absorbed a concurrent-DDL race on {Dialect} step {Step} ({Detail}); re-running it once in a fresh transaction."
    )]
    private static partial void LogRaceAbsorbed(ILogger logger, string dialect, string step, string detail);

    [LoggerMessage(
        EventId = 3,
        EventName = "SchemaRunnerLockReleaseFailed",
        Level = LogLevel.Warning,
        Message = "Schema runner failed to release its session lock {LockResource}; closing the connection releases it."
    )]
    private static partial void LogLockReleaseFailed(ILogger logger, string lockResource, Exception exception);

    [LoggerMessage(
        EventId = 4,
        EventName = "SchemaRunnerHistoryAbsent",
        Level = LogLevel.Debug,
        Message = "Schema runner found no {Dialect} history table in schema {Schema}; every registered step is missing."
    )]
    private static partial void LogHistoryAbsent(ILogger logger, string dialect, string schema);
}
#pragma warning restore CA2100
