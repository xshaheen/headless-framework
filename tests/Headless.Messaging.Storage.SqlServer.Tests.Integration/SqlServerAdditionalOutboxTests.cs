// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerAdditionalOutboxTests(SqlServerTestFixture fixture) : AdditionalOutboxConformanceTests
{
    protected override async Task<string> CreateDatabaseAsync(string name)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync($"CREATE DATABASE [{name}];");

        // A database recreated under an earlier name must not be refused by a pool still blocking on the login
        // failures of its absence (SqlClient's pool blocking period).
        SqlConnection.ClearAllPools();

        return new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name }.ConnectionString;
    }

    protected override async Task DropDatabaseAsync(string name)
    {
        // Pooled connections of this process still point at the database; drop them before the server drops it.
        SqlConnection.ClearAllPools();

        // A running relay still polls the database, and forcing it to single-user can deadlock with those
        // sessions. The drop takes deadlock priority, and a rare victim of it retries. A relay whose session the
        // ALTER killed retries at once and can take the one single-user connection before the DROP, which fails the
        // drop as in use and every later ALTER as single-user and occupied; each attempt therefore kills the
        // database's sessions first.
        const int deadlockVictim = 1205;
        const int databaseInUse = 3702;
        const int singleUserOccupied = 5064;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(fixture.ConnectionString);
                await connection.ExecuteAsync(
                    $"""
                    SET DEADLOCK_PRIORITY HIGH;
                    IF DB_ID(N'{name}') IS NOT NULL
                    BEGIN
                        DECLARE @kill nvarchar(max) = N'';
                        SELECT @kill += N'KILL ' + CONVERT(nvarchar(11), session_id) + N';'
                        FROM sys.dm_exec_sessions
                        WHERE database_id = DB_ID(N'{name}') AND session_id <> @@SPID;
                        EXEC (@kill);
                        ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{name}];
                    END;
                    """
                );

                return;
            }
            catch (SqlException ex)
                when (ex.Errors.Cast<SqlError>()
                        .Any(e => e.Number is deadlockVictim or databaseInUse or singleUserOccupied)
                    && attempt < 10
                )
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    protected override void UseDatabase(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString);

    protected override void UsePrimaryStorage<TContext>(MessagingSetupBuilder setup) =>
        setup.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage<TContext>(OutboxStorageBuilder outbox) =>
        outbox.UseEntityFramework<TContext>();

    protected override MessagingSetupBuilder UseOutboxStorage(OutboxStorageBuilder outbox, string connectionString) =>
        outbox.UseSqlServer(connectionString);

    protected override async Task<IReadOnlyList<PublishedRow>> ReadPublishedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string? Content, string StatusName)>(
            "SELECT [Content], [StatusName] FROM [headless].[MessagingPublished];"
        );

        return rows.Select(row => new PublishedRow(row.Content, row.StatusName)).ToList();
    }

    protected override async Task<bool> HasReceivedTableAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<bool>(
            "SELECT CAST(CASE WHEN OBJECT_ID(N'headless.MessagingReceived', N'U') IS NULL THEN 0 ELSE 1 END AS bit);"
        );
    }

    protected override async Task ExecuteScriptAsync(string connectionString, string script)
    {
        // GO is a client-side separator that SQL Server itself rejects, so each batch between them is its own command.
        await using var connection = new SqlConnection(connectionString);
        var batch = new StringBuilder();

        foreach (var line in script.Split('\n'))
        {
            if (!string.Equals(line.Trim(), "GO", StringComparison.Ordinal))
            {
                batch.AppendLine(line);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(batch.ToString()))
            {
                await connection.ExecuteAsync(batch.ToString());
            }

            batch.Clear();
        }

        if (!string.IsNullOrWhiteSpace(batch.ToString()))
        {
            await connection.ExecuteAsync(batch.ToString());
        }
    }
}
