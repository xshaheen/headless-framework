// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Npgsql;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// The databases one fixture owns on a PostgreSQL server it may share: a session advisory lock taken from the
/// <c>postgres</c> database marks the owner alive, and a sweep at startup drops the databases of owners whose lock is
/// free.
/// </summary>
#pragma warning disable CA2100 // False positive: SQL text interpolates only owner-generated database names (hf_ plus hex, or a checked suffix); values are parameters.
internal sealed class PostgreSqlDatabaseOwnership : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly NpgsqlConnection _lockConnection;

    private PostgreSqlDatabaseOwnership(string adminConnectionString, string ownerId, NpgsqlConnection lockConnection)
    {
        _adminConnectionString = adminConnectionString;
        OwnerId = ownerId;
        _lockConnection = lockConnection;
    }

    /// <summary>The owner id, which is also the name of the fixture's own database.</summary>
    public string OwnerId { get; }

    /// <summary>Sweeps the databases of dead owners, takes a new owner's lock, and creates its database.</summary>
    /// <param name="adminConnectionString">A superuser connection string to the <c>postgres</c> database.</param>
    /// <param name="cancellationToken">Cancels the setup.</param>
    public static async Task<PostgreSqlDatabaseOwnership> CreateAsync(
        string adminConnectionString,
        CancellationToken cancellationToken
    )
    {
        // The lock lives exactly as long as this session, so the connection must never return to a pool where it
        // would outlive the fixture or be reset under it.
        var unpooled = new NpgsqlConnectionStringBuilder(adminConnectionString) { Pooling = false }.ToString();
        var ownerId = TestDatabaseOwner.NewId();
        var lockConnection = new NpgsqlConnection(unpooled);

        try
        {
            await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // The lock comes before the database, so no sweep ever sees this owner's database with its lock free.
            if (!await _TryLockAsync(lockConnection, ownerId, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Could not take the owner lock of new test database {ownerId}.");
            }

            var ownership = new PostgreSqlDatabaseOwnership(unpooled, ownerId, lockConnection);
            await ownership._SweepDeadOwnersAsync(cancellationToken).ConfigureAwait(false);
            await ownership.CreateDatabaseAsync(ownerId, cancellationToken).ConfigureAwait(false);

            return ownership;
        }
        catch
        {
            await lockConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates <paramref name="database"/> unless it exists. The name must belong to this owner.</summary>
    public async Task CreateDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        if (!string.Equals(TestDatabaseOwner.OwnerOf(database), OwnerId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Database {database} does not belong to owner {OwnerId}.", nameof(database));
        }

        var connection = new NpgsqlConnection(_adminConnectionString);
        await using var _ = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name;", connection);
        await using (exists.ConfigureAwait(false))
        {
            exists.Parameters.AddWithValue("name", database);

            if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return;
            }
        }

        var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\";", connection);
        await using (create.ConfigureAwait(false))
        {
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Drops every database of this owner, then releases its lock.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _DropOwnedDatabasesAsync(OwnerId, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _lockConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task _SweepDeadOwnersAsync(CancellationToken cancellationToken)
    {
        var owners = (await _ListOwnedDatabasesAsync(cancellationToken).ConfigureAwait(false))
            .Select(TestDatabaseOwner.OwnerOf)
            .OfType<string>()
            .Where(owner => !string.Equals(owner, OwnerId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var owner in owners)
        {
            await _SweepIfDeadAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _SweepIfDeadAsync(string owner, CancellationToken cancellationToken)
    {
        var probe = new NpgsqlConnection(_adminConnectionString);
        await using var _ = probe.ConfigureAwait(false);
        await probe.OpenAsync(cancellationToken).ConfigureAwait(false);

        // A live owner still holds its lock. Taking it proves the owner's session is gone, and holding it while
        // dropping keeps a second sweeper from racing this one over the same databases.
        if (await _TryLockAsync(probe, owner, cancellationToken).ConfigureAwait(false))
        {
            await _DropOwnedDatabasesAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<string>> _ListOwnedDatabasesAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_adminConnectionString);
        await using var _ = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE starts_with(datname, @prefix);",
            connection
        );
        await using var __ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("prefix", TestDatabaseOwner.Prefix);

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var ___ = reader.ConfigureAwait(false);

        var names = new List<string>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task _DropOwnedDatabasesAsync(string owner, CancellationToken cancellationToken)
    {
        var databases = (await _ListOwnedDatabasesAsync(cancellationToken).ConfigureAwait(false))
            .Where(name => string.Equals(TestDatabaseOwner.OwnerOf(name), owner, StringComparison.Ordinal))
            .ToList();

        if (databases.Count == 0)
        {
            return;
        }

        var connection = new NpgsqlConnection(_adminConnectionString);
        await using var _ = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var database in databases)
        {
            // FORCE ends any session still in the database, which DROP DATABASE otherwise refuses.
            var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);", connection);
            await using (drop.ConfigureAwait(false))
            {
                drop.CommandTimeout = 120;
                await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> _TryLockAsync(
        NpgsqlConnection connection,
        string owner,
        CancellationToken cancellationToken
    )
    {
        var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@name, 0));", connection);
        await using var _ = command.ConfigureAwait(false);
        command.Parameters.AddWithValue("name", TestDatabaseOwner.LockName(owner));

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }
}
#pragma warning restore CA2100
