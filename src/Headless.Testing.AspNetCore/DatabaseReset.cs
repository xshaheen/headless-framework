// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Respawner-based database reset helper. Wraps <see cref="Respawner"/>, always preserving the EF Core migrations
/// history table and the Headless schema history table, plus any tables listed in
/// <see cref="DatabaseResetOptions.TablesToPreserve"/>. Created with the host's service provider, it also preserves
/// the host-state tables framework features declare (see <see cref="DatabaseResetOptions.PreserveHostStateTables"/>).
/// </summary>
/// <remarks>
/// <para>
/// <c>CreateAsync</c> must be called <b>after</b> migrations have been applied — Respawner
/// introspects the live schema to build its deletion graph.
/// </para>
/// <para>
/// This class is usable standalone (without <see cref="HeadlessTestServer{TProgram}"/>).
/// The connection passed to <c>CreateAsync</c> and <see cref="ResetAsync"/> must already
/// be open.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DatabaseReset
{
    private readonly Respawner _respawner;

    private DatabaseReset(Respawner respawner) => _respawner = respawner;

    /// <summary>
    /// Creates a <see cref="DatabaseReset"/> instance by building a <see cref="Respawner"/> against
    /// the provided open <paramref name="connection"/>, preserving each registered feature's schema history table under
    /// the name its dialect stores it by, and the host-state tables the features in <paramref name="services"/> declare.
    /// </summary>
    /// <remarks>
    /// Use this overload whenever the database belongs to a running host, including a host started by a hand-written
    /// <c>WebApplicationFactory</c>: feature, permission, and setting definitions and cluster membership rows are
    /// written once at startup or kept live by the host, so a reset that wipes them breaks every later test.
    /// </remarks>
    /// <param name="connection">An <b>open</b> <see cref="DbConnection"/>.</param>
    /// <param name="services">
    /// The host's root service provider. Its <see cref="SchemaContribution"/> registrations name the host-state tables.
    /// </param>
    /// <param name="options">
    /// Optional configuration. When <see langword="null"/>, defaults to the Postgres adapter with the two history
    /// tables and the declared host-state tables preserved.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel Respawn by closing the active connection. When omitted,
    /// <see cref="Xunit.TestContext.Current"/> supplies the active test's cancellation token.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="connection"/> is not in the <see cref="System.Data.ConnectionState.Open"/> state.
    /// </exception>
    public static Task<DatabaseReset> CreateAsync(
        DbConnection connection,
        IServiceProvider services,
        DatabaseResetOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);
        options ??= new DatabaseResetOptions();

        List<Table> keptTables = [];

        foreach (var contribution in services.GetServices<SchemaContribution>())
        {
            // The dialect names the history table: on a database without schemas the name carries the schema as a
            // prefix, which the schema-less always-kept entry cannot match. Kept whatever the options say, because the
            // runner trusts the history and would never recreate a table whose step it still records.
            keptTables.Add(new Table(contribution.Schema, contribution.Dialect.HistoryTableName(contribution.Schema)));

            if (options.PreserveHostStateTables)
            {
                foreach (var table in contribution.HostStateTables)
                {
                    keptTables.Add(new Table(contribution.Schema, table));
                }
            }
        }

        return _CreateAsync(connection, options, keptTables, cancellationToken);
    }

    /// <summary>
    /// Creates a <see cref="DatabaseReset"/> instance by building a <see cref="Respawner"/> against
    /// the provided open <paramref name="connection"/>, for a database no host owns.
    /// </summary>
    /// <remarks>
    /// Without a service provider this overload cannot see the host-state tables features declare, or the schema each
    /// feature's history table is named after, so it preserves only <c>__EFMigrationsHistory</c>,
    /// <c>headless_schema_history</c>, and <see cref="DatabaseResetOptions.TablesToPreserve"/>. On SQLite, where the
    /// history table's name carries the schema as a prefix, that misses the history. For a database a running host
    /// uses, call the overload that takes the host's <see cref="IServiceProvider"/>.
    /// </remarks>
    /// <param name="connection">An <b>open</b> <see cref="DbConnection"/>.</param>
    /// <param name="options">
    /// Optional configuration. When <see langword="null"/>, defaults to the Postgres adapter with only the two history
    /// tables preserved.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel Respawn by closing the active connection. When omitted,
    /// <see cref="Xunit.TestContext.Current"/> supplies the active test's cancellation token.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="connection"/> is not in the <see cref="System.Data.ConnectionState.Open"/> state.
    /// </exception>
    public static Task<DatabaseReset> CreateAsync(
        DbConnection connection,
        DatabaseResetOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _CreateAsync(connection, options ?? new DatabaseResetOptions(), [], cancellationToken);
    }

    private static async Task<DatabaseReset> _CreateAsync(
        DbConnection connection,
        DatabaseResetOptions options,
        List<Table> keptTables,
        CancellationToken cancellationToken
    )
    {
        Ensure.True(connection.State == ConnectionState.Open, "Connection must be open to create Respawner.");

        // Schema-less entries match the name in every schema. Both history tables record work done to a schema that a
        // data reset leaves in place, so wiping them makes the next startup replay that work.
        var tablesToIgnore = new List<Table>(options.TablesToPreserve.Count + keptTables.Count + 2)
        {
            new(HistoryRepository.DefaultTableName),
            new(SchemaRunner.HistoryTableName),
        };
        tablesToIgnore.AddRange(keptTables);
        tablesToIgnore.AddRange(options.TablesToPreserve);

        var respawner = await DatabaseResetOperation
            .RunAsync(
                connection,
                () =>
                    Respawner.CreateAsync(
                        connection,
                        new RespawnerOptions { TablesToIgnore = [.. tablesToIgnore], DbAdapter = options.DbAdapter }
                    ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new DatabaseReset(respawner);
    }

    /// <summary>
    /// Deletes all data from non-excluded tables using the provided open <paramref name="connection"/>.
    /// </summary>
    /// <param name="connection">An <b>open</b> <see cref="DbConnection"/>.</param>
    /// <param name="cancellationToken">
    /// Token used to cancel Respawn by closing the active connection. When omitted,
    /// <see cref="Xunit.TestContext.Current"/> supplies the active test's cancellation token.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="connection"/> is not in the <see cref="System.Data.ConnectionState.Open"/> state.
    /// </exception>
    public async Task ResetAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        Ensure.True(connection.State == ConnectionState.Open, "Connection must be open to reset database.");

        await DatabaseResetOperation
            .RunAsync(connection, () => _respawner.ResetAsync(connection), cancellationToken)
            .ConfigureAwait(false);
    }
}
