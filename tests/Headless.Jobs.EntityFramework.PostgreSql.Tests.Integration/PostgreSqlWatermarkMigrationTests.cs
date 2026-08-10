// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tests.Migrations.PostgreSql;

namespace Tests;

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlWatermarkMigrationTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task watermark_migration_backfills_safe_defaults_and_blocks_state_losing_downgrade()
    {
        var cancellationToken = AbortToken;
        var cronJobId = Guid.NewGuid();
        await fixture.ResetDatabaseAsync(cancellationToken);
        await _ExecuteAsync("DROP TABLE IF EXISTS \"__WatermarkMigrationsHistory\";", cancellationToken);
        await _CreateLegacySchemaAsync(cronJobId, cancellationToken);

        var options = new DbContextOptionsBuilder<PostgreSqlWatermarkMigrationDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                sql =>
                    sql.MigrationsAssembly(typeof(PostgreSqlAddCronScheduleWatermark).Assembly.FullName)
                        .MigrationsHistoryTable("__WatermarkMigrationsHistory")
            )
            .Options;
        await using var dbContext = new PostgreSqlWatermarkMigrationDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(PostgreSqlAddCronScheduleWatermark.Id, cancellationToken);
        (await _ReadWatermarkDefaultsAsync(cronJobId, cancellationToken))
            .Should()
            .Be((default, default, 0, "Coalesce", null, 0, null));

        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);
        (await _ColumnExistsAsync("CronJobs", "ReconciledThroughUtc", cancellationToken)).Should().BeFalse();

        await migrator.MigrateAsync(PostgreSqlAddCronScheduleWatermark.Id, cancellationToken);
        await _ExecuteAsync(
            "UPDATE jobs.\"CronJobs\" SET \"ReconciledThroughUtc\" = now(), \"NextDueUtc\" = now() + interval '1 minute' WHERE \"Id\" = @id;",
            cancellationToken,
            cronJobId
        );

        var downgrade = () => migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);
        await downgrade
            .Should()
            .ThrowAsync<Exception>()
            .WithMessage("*Cannot downgrade cron schedule watermark migration*");
        (await _ReadWatermarkDefaultsAsync(cronJobId, cancellationToken)).ReconciledThroughUtc.Should().NotBe(default);

        await _ExecuteAsync(
            "UPDATE jobs.\"CronJobs\" SET \"ReconciledThroughUtc\" = '-infinity', \"NextDueUtc\" = '-infinity', "
                + "\"FingerprintFailureCount\" = 1, \"FingerprintRetryAfterUtc\" = now() + interval '1 hour' WHERE \"Id\" = @id;",
            cancellationToken,
            cronJobId
        );
        await downgrade
            .Should()
            .ThrowAsync<Exception>()
            .WithMessage("*Cannot downgrade cron schedule watermark migration*");
        (await _ReadWatermarkDefaultsAsync(cronJobId, cancellationToken)).FingerprintFailureCount.Should().Be(1);
    }

    [Fact]
    public async Task watermark_migration_creates_the_fingerprint_retry_keyset_index()
    {
        var cancellationToken = AbortToken;
        var cronJobId = Guid.NewGuid();
        await fixture.ResetDatabaseAsync(cancellationToken);
        await _ExecuteAsync("DROP TABLE IF EXISTS \"__WatermarkMigrationsHistory\";", cancellationToken);
        await _CreateLegacySchemaAsync(cronJobId, cancellationToken);
        var options = new DbContextOptionsBuilder<PostgreSqlWatermarkMigrationDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                sql =>
                    sql.MigrationsAssembly(typeof(PostgreSqlAddCronScheduleWatermark).Assembly.FullName)
                        .MigrationsHistoryTable("__WatermarkMigrationsHistory")
            )
            .Options;
        await using var dbContext = new PostgreSqlWatermarkMigrationDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PostgreSqlAddCronScheduleWatermark.Id, cancellationToken);
        await migrator.MigrateAsync(PostgreSqlAddCronScheduleWatermark.Id, cancellationToken);

        (await _IndexExistsAsync("IX_CronJobs_FingerprintRetryAfterUtc_Id", cancellationToken)).Should().BeTrue();
    }

    private async Task<bool> _IndexExistsAsync(string indexName, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'jobs' AND indexname = @name);";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task _CreateLegacySchemaAsync(Guid cronJobId, CancellationToken cancellationToken)
    {
        await _ExecuteAsync(
            """
            CREATE SCHEMA jobs;
            CREATE TABLE jobs."CronJobs" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "IsPaused" boolean NOT NULL DEFAULT FALSE
            );
            CREATE TABLE jobs."CronJobOccurrences" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "CronJobId" uuid NOT NULL,
                "ExecutionTime" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "Status" character varying(32) NOT NULL
            );
            INSERT INTO jobs."CronJobs" ("Id") VALUES (@id);
            """,
            cancellationToken,
            cronJobId
        );
    }

    private async Task<(
        DateTime ReconciledThroughUtc,
        DateTime NextDueUtc,
        int MissedRunGraceSeconds,
        string OnMissedRun,
        string? EvaluationFingerprint,
        int FingerprintFailureCount,
        DateTime? FingerprintRetryAfterUtc
    )> _ReadWatermarkDefaultsAsync(Guid cronJobId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"ReconciledThroughUtc\", \"NextDueUtc\", \"MissedRunGraceSeconds\", \"OnMissedRun\", "
            + "\"EvaluationFingerprint\", \"FingerprintFailureCount\", \"FingerprintRetryAfterUtc\" "
            + "FROM jobs.\"CronJobs\" WHERE \"Id\" = @id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = cronJobId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).Should().BeTrue();
        return (
            reader.GetDateTime(0),
            reader.GetDateTime(1),
            reader.GetInt32(2),
            reader.GetString(3),
            await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
            reader.GetInt32(5),
            await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetDateTime(6)
        );
    }

    private async Task<bool> _ColumnExistsAsync(
        string tableName,
        string columnName,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns "
            + "WHERE table_schema = 'jobs' AND table_name = @tableName AND column_name = @columnName);";
        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "@tableName";
        tableParameter.Value = tableName;
        command.Parameters.Add(tableParameter);
        var columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "@columnName";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, Guid? id = null)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = id.Value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
