// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Microsoft.EntityFrameworkCore.Migrations;
using Respawn;
using Respawn.Graph;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Respawner-based database reset helper. Wraps <see cref="Respawner"/>, always preserving the EF Core migrations
/// history table and the Headless schema history table, plus any tables listed in
/// <see cref="DatabaseResetOptions.TablesToPreserve"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreateAsync"/> must be called <b>after</b> migrations have been applied — Respawner
/// introspects the live schema to build its deletion graph.
/// </para>
/// <para>
/// This class is usable standalone (without <see cref="HeadlessTestServer{TProgram}"/>).
/// The connection passed to <see cref="CreateAsync"/> and <see cref="ResetAsync"/> must already
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
    /// the provided open <paramref name="connection"/>.
    /// </summary>
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
    public static async Task<DatabaseReset> CreateAsync(
        DbConnection connection,
        DatabaseResetOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Ensure.True(connection.State == ConnectionState.Open, "Connection must be open to create Respawner.");

        options ??= new DatabaseResetOptions();

        // Schema-less entries match the name in every schema. Both history tables record work done to a schema that a
        // data reset leaves in place, so wiping them makes the next startup replay that work.
        var tablesToIgnore = new List<Table>(options.TablesToPreserve.Count + 2)
        {
            new(HistoryRepository.DefaultTableName),
            new(SchemaRunner.HistoryTableName),
        };
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
