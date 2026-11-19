// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.Sqlite;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// A verify on a pooled connection must not read "no history table" as "nothing applied". A SQLite connection caches
/// the schema it last read. When another replica created the history table since, and a writer holds the database at
/// the moment of the read, SQLite cannot re-read its schema and reports the table missing although it exists.
/// </summary>
public sealed class SqliteSchemaRunnerHistoryVisibilityTests : TestBase
{
    private const string _Schema = "visibility";

    [Fact]
    public async Task should_not_report_applied_steps_missing_when_a_writer_hides_the_history_from_a_stale_connection()
    {
        await using var database = SqliteTestDatabase.Create();
        var pooled = database.ConnectionString;
        var unpooled = new SqliteConnectionStringBuilder(pooled) { Pooling = false }.ToString();

        // A pooled connection reads the schema before the history table exists, then goes back to the pool, where the
        // verifying runner picks it up again.
        await using (var early = new SqliteConnection(pooled))
        {
            await early.OpenAsync(AbortToken);
            await using var read = early.CreateCommand();
            read.CommandText = "SELECT count(*) FROM sqlite_schema;";
            await read.ExecuteScalarAsync(AbortToken);
        }

        // Another replica applies the step and records it.
        (await _Runner(unpooled).ApplyAsync(AbortToken))
            .AppliedSteps.Should()
            .ContainSingle();

        // A writer holds the database while the verify reads.
        await using var writer = new SqliteConnection(unpooled);
        await writer.OpenAsync(AbortToken);
        await using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN EXCLUSIVE;";
            await begin.ExecuteNonQueryAsync(AbortToken);
        }

        // The writer outlasts the one-second command timeout, so the history cannot be read: the verify must say so,
        // not report the recorded step as missing.
        var verify = () => _Runner(pooled, commandTimeout: TimeSpan.FromSeconds(1)).VerifyAsync(AbortToken);

        (await verify.Should().ThrowAsync<SchemaRunnerException>())
            .WithInnerException<SqliteException>()
            .Which.SqliteErrorCode.Should()
            .Be(SQLitePCL.raw.SQLITE_BUSY);
    }

    private static SchemaRunner _Runner(string connectionString, TimeSpan? commandTimeout = null)
    {
        var contribution = new SchemaContribution(
            "Visibility",
            SqliteSchemaDialect.Instance,
            () => new SqliteConnection(connectionString),
            _Schema,
            [
                new SchemaStep(
                    "1",
                    "Creates the visibility table.",
                    $"CREATE TABLE IF NOT EXISTS \"{_Schema}_items\" (id INTEGER NOT NULL PRIMARY KEY);"
                ),
            ]
        );

        return new SchemaRunner([contribution], commandTimeout: commandTimeout);
    }
}
