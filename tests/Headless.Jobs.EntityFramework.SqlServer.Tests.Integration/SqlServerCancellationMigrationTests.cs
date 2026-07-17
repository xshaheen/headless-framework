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
    public async Task cancellation_migration_round_trips_without_touching_other_schema()
    {
        var cancellationToken = AbortToken;
        var jobId = Guid.NewGuid();
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
                sql => sql.MigrationsAssembly(typeof(AddTimeJobCancellationRequest).Assembly.FullName)
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
