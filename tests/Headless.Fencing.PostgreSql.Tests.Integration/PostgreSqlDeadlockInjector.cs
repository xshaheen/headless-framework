// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

/// <summary>
/// Makes the next writes to one table fail with SQLSTATE 40P01, the code PostgreSQL raises for a real deadlock, so a
/// test can prove how the store retries without racing two sessions into an actual deadlock.
/// </summary>
/// <remarks>
/// A statement-level trigger raises the error while a counter sequence is below the armed number of failures. The
/// counter is a sequence because <c>nextval</c> survives the rollback the error causes, so it counts every attempt that
/// reached the table: each failing attempt consumes exactly one value.
/// </remarks>
internal sealed class PostgreSqlDeadlockInjector(string connectionString, string schema, string table)
{
    private string Counter => $"\"{schema}\".injected_deadlocks";

    public async Task ArmAsync(long failures, CancellationToken cancellationToken)
    {
        await _ExecuteAsync(
            $"""
            CREATE SEQUENCE IF NOT EXISTS {Counter};
            ALTER SEQUENCE {Counter} RESTART;
            CREATE OR REPLACE FUNCTION "{schema}".inject_deadlock() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF nextval('{Counter}') <= {failures} THEN
                    RAISE EXCEPTION 'injected deadlock' USING ERRCODE = 'deadlock_detected';
                END IF;
                RETURN NULL;
            END
            $fn$;
            DROP TRIGGER IF EXISTS inject_deadlock ON "{schema}"."{table}";
            CREATE TRIGGER inject_deadlock BEFORE INSERT OR UPDATE OR DELETE ON "{schema}"."{table}"
                FOR EACH STATEMENT EXECUTE FUNCTION "{schema}".inject_deadlock();
            """,
            cancellationToken
        );
    }

    /// <summary>Drops the schema so the next host recreates the table without the trigger.</summary>
    public Task DropSchemaAsync(CancellationToken cancellationToken)
    {
        return _ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", cancellationToken);
    }

    /// <summary>How many statements have reached the table since the injector was armed.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM {Counter}",
            connection
        );

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Waits in real time until at least <paramref name="count" /> statements have reached the table.</summary>
    public async Task WaitForCountAsync(long count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (await CountAsync(timeout.Token) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    /// <summary>
    /// Moves the fake clock forward in small steps until <paramref name="done" /> holds. Stepping, rather than one jump,
    /// covers a delay the store starts on the clock only after the test has already moved it.
    /// </summary>
    public static async Task AdvanceUntilAsync(
        FakeTimeProvider clock,
        Func<Task<bool>> done,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!await done())
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
