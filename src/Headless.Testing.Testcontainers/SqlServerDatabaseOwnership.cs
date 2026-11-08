// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.SqlClient;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// The databases one fixture owns on a SQL Server it may share: a session application lock on <c>master</c> marks the
/// owner alive, and a sweep at startup drops the databases of owners whose lock is free.
/// </summary>
#pragma warning disable CA2100 // False positive: SQL text interpolates only owner-generated database names (hf_ plus hex, or a checked suffix); values are parameters.
internal sealed class SqlServerDatabaseOwnership : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly SqlConnection _lockConnection;

    private SqlServerDatabaseOwnership(string adminConnectionString, string ownerId, SqlConnection lockConnection)
    {
        _adminConnectionString = adminConnectionString;
        OwnerId = ownerId;
        _lockConnection = lockConnection;
    }

    /// <summary>The owner id, which is also the name of the fixture's own database.</summary>
    public string OwnerId { get; }

    /// <summary>
    /// Sweeps the databases of dead owners, takes a new owner's lock, and creates its database.
    /// </summary>
    /// <param name="adminConnectionString">A <c>sa</c> connection string to <c>master</c>.</param>
    /// <param name="cancellationToken">Cancels the setup.</param>
    public static async Task<SqlServerDatabaseOwnership> CreateAsync(
        string adminConnectionString,
        CancellationToken cancellationToken
    )
    {
        // The lock lives exactly as long as this session, so the connection must never return to a pool where it
        // would outlive the fixture or be reset under it.
        var lockConnectionString = new SqlConnectionStringBuilder(adminConnectionString) { Pooling = false }.ToString();
        var ownerId = TestDatabaseOwner.NewId();
        var lockConnection = new SqlConnection(lockConnectionString);

        try
        {
            await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // The lock comes before the database, so no sweep ever sees this owner's database with its lock free.
            if (!await _TryLockAsync(lockConnection, ownerId, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Could not take the owner lock of new test database {ownerId}.");
            }

            var ownership = new SqlServerDatabaseOwnership(lockConnectionString, ownerId, lockConnection);
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
        if (TestDatabaseOwner.OwnerOf(database) != OwnerId)
        {
            throw new ArgumentException($"Database {database} does not belong to owner {OwnerId}.", nameof(database));
        }

        await using var connection = new SqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}];",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            .Where(owner => owner != OwnerId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var owner in owners)
        {
            await _SweepIfDeadAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _SweepIfDeadAsync(string owner, CancellationToken cancellationToken)
    {
        var probe = new SqlConnection(_adminConnectionString);
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
        await using var connection = new SqlConnection(_adminConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            $"SELECT name FROM sys.databases WHERE name LIKE N'{TestDatabaseOwner.Prefix.Replace("_", "[_]", StringComparison.Ordinal)}%';",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

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
            .Where(name => TestDatabaseOwner.OwnerOf(name) == owner)
            .ToList();

        foreach (var database in databases)
        {
            await using var connection = new SqlConnection(_adminConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // SINGLE_USER WITH ROLLBACK IMMEDIATE ends any session still in the database, which DROP DATABASE
            // otherwise waits on or refuses. A database a test left OFFLINE has no sessions and refuses the ALTER, but
            // drops as it is.
            await using var command = new SqlCommand(
                $"""
                IF DB_ID(N'{database}') IS NOT NULL
                BEGIN
                    IF DATABASEPROPERTYEX(N'{database}', 'Status') = 'ONLINE'
                        ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{database}];
                END
                """,
                connection
            );
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> _TryLockAsync(
        SqlConnection connection,
        string owner,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
            SELECT @result;
            """,
            connection
        );
        command.Parameters.AddWithValue("@resource", TestDatabaseOwner.LockName(owner));

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        // 0 granted, 1 granted after a wait; negative values are a timeout, cancel, or deadlock.
        return result is int code && code >= 0;
    }
}
#pragma warning restore CA2100
