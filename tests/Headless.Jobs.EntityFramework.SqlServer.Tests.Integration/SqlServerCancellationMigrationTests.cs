// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tests.Migrations;

namespace Tests;

[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerCancellationMigrationTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task should_preserve_existing_rows_and_block_destructive_downgrade_when_cron_control_migrates()
    {
        var cancellationToken = AbortToken;
        var cronJobId = Guid.NewGuid();
        var executionTime = new DateTime(2026, 7, 17, 10, 30, 0, DateTimeKind.Utc);
        await _DropMigrationHistoryAsync("__CronControlMigrationsHistory", cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'jobs') EXEC('CREATE SCHEMA [jobs]');"
                + "CREATE TABLE [jobs].[TimeJobs] ([Id] uniqueidentifier NOT NULL PRIMARY KEY);"
                + "CREATE TABLE [jobs].[CronJobs] ([Id] uniqueidentifier NOT NULL PRIMARY KEY);"
                + "CREATE TABLE [jobs].[CronJobOccurrences] ("
                + "[Id] uniqueidentifier NOT NULL PRIMARY KEY, [CronJobId] uniqueidentifier NOT NULL, "
                + "[ExecutionTime] datetime2 NOT NULL, [Status] nvarchar(32) NOT NULL);"
                + "CREATE UNIQUE INDEX [UQ_CronJobId_ExecutionTime] ON [jobs].[CronJobOccurrences] ([CronJobId], [ExecutionTime]);"
                + "INSERT INTO [jobs].[CronJobs] ([Id]) VALUES (@cronJobId);";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@cronJobId";
            parameter.Value = cronJobId;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var options = new DbContextOptionsBuilder<SqlServerCancellationMigrationDbContext>()
            .UseSqlServer(
                fixture.ConnectionString,
                sql =>
                    sql.MigrationsAssembly(typeof(AddCronPauseAndTimeZone).Assembly.FullName)
                        .MigrationsHistoryTable("__CronControlMigrationsHistory")
            )
            .Options;
        await using var dbContext = new SqlServerCancellationMigrationDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(AddCronPauseAndTimeZone.Id, cancellationToken);

        (await _ReadCronDefaultsAsync(cronJobId, cancellationToken)).Should().Be((false, 0L, null));
        await _InsertOccurrenceAsync(cronJobId, executionTime, "Skipped", cancellationToken);
        await _InsertOccurrenceAsync(cronJobId, executionTime, "Idle", cancellationToken);

        var downgrade = () => migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);
        await downgrade
            .Should()
            .ThrowAsync<Exception>()
            .WithMessage("*Cannot downgrade cron pause/timezone migration*");
        (await _ReadCronDefaultsAsync(cronJobId, cancellationToken)).Should().Be((false, 0L, null));
    }

    [Fact]
    public async Task cancellation_migration_round_trips_without_touching_other_schema()
    {
        var cancellationToken = AbortToken;
        var jobId = Guid.NewGuid();
        await _DropMigrationHistoryAsync("__CancellationMigrationsHistory", cancellationToken);
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using (var connection = fixture.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'jobs') EXEC('CREATE SCHEMA [jobs]');"
                + "CREATE TABLE [jobs].[TimeJobs] ([Id] uniqueidentifier NOT NULL PRIMARY KEY);"
                + "INSERT INTO [jobs].[TimeJobs] ([Id]) VALUES (@id);";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = jobId;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var options = new DbContextOptionsBuilder<SqlServerCancellationMigrationDbContext>()
            .UseSqlServer(
                fixture.ConnectionString,
                sql =>
                    sql.MigrationsAssembly(typeof(AddTimeJobCancellationRequest).Assembly.FullName)
                        .MigrationsHistoryTable("__CancellationMigrationsHistory")
            )
            .Options;
        await using var dbContext = new SqlServerCancellationMigrationDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(AddTimeJobCancellationRequest.Id, cancellationToken);
        (await _ReadColumnAsync(cancellationToken)).Should().BeFalse();

        await migrator.MigrateAsync(Migration.InitialDatabase, cancellationToken);
        (await _ColumnExistsAsync(cancellationToken)).Should().BeFalse();
        (await _TimeJobExistsAsync(jobId, cancellationToken)).Should().BeTrue();
    }

    private async Task<bool> _ReadColumnAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) [CancelRequested] FROM [jobs].[TimeJobs]";
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? true);
    }

    private async Task _DropMigrationHistoryAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS [dbo].[{tableName}]";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(bool IsPaused, long Revision, string? TimeZoneId)> _ReadCronDefaultsAsync(
        Guid cronJobId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [IsPaused], [ScheduleRevision], [TimeZoneId] FROM [jobs].[CronJobs] WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = cronJobId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        (await reader.ReadAsync(cancellationToken)).Should().BeTrue();
        return (
            reader.GetBoolean(0),
            reader.GetInt64(1),
            await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2)
        );
    }

    private async Task _InsertOccurrenceAsync(
        Guid cronJobId,
        DateTime executionTime,
        string status,
        CancellationToken cancellationToken
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO [jobs].[CronJobOccurrences] ([Id], [CronJobId], [ExecutionTime], [Status]) "
            + "VALUES (@id, @cronJobId, @executionTime, @status)";
        foreach (
            var (name, value) in new (string, object)[]
            {
                ("@id", Guid.NewGuid()),
                ("@cronJobId", cronJobId),
                ("@executionTime", executionTime),
                ("@status", status),
            }
        )
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> _ColumnExistsAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CASE WHEN COL_LENGTH('jobs.TimeJobs', 'CancelRequested') IS NULL THEN 0 ELSE 1 END";
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<bool> _TimeJobExistsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM [jobs].[TimeJobs] WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = jobId;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }
}
