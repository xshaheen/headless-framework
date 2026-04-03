// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Migrations;
using Respawn;
using Respawn.Graph;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Respawner-based database reset helper. Wraps <see cref="Respawner"/> with automatic exclusion
/// of the EF Core migrations history table and configurable additional exclusions.
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
    /// Optional configuration. When <c>null</c>, defaults to Postgres adapter with only the
    /// EF migrations history table excluded.
    /// </param>
    public static async Task<DatabaseReset> CreateAsync(DbConnection connection, DatabaseResetOptions? options = null)
    {
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to create Respawner.");
        }

        options ??= new DatabaseResetOptions();

        var tablesToIgnore = new List<Table>(options.TablesToIgnore.Count + 1)
        {
            new(HistoryRepository.DefaultTableName),
        };
        tablesToIgnore.AddRange(options.TablesToIgnore);

        var respawner = await Respawner
            .CreateAsync(
                connection,
                new RespawnerOptions { TablesToIgnore = [.. tablesToIgnore], DbAdapter = options.DbAdapter }
            )
            .ConfigureAwait(false);

        return new DatabaseReset(respawner);
    }

    /// <summary>
    /// Deletes all data from non-excluded tables using the provided open <paramref name="connection"/>.
    /// </summary>
    /// <param name="connection">An <b>open</b> <see cref="DbConnection"/>.</param>
    public Task ResetAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Connection must be open to reset database.");
        }

        return _respawner.ResetAsync(connection);
    }
}
