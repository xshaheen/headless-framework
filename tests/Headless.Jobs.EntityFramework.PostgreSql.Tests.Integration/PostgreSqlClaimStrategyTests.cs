// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlClaimStrategyTests(PostgreSqlJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task locked_candidate_is_skipped_while_an_unlocked_root_is_claimed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("skip-locked-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var locked = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "locked",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-2),
            };
            var available = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "available",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([locked, available], ct);

            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT \"Id\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Id\" = @id FOR UPDATE;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = locked.Id;
                command.Parameters.Add(parameter);
                await command.ExecuteScalarAsync(ct);
            }

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToListAsync(ct);
            claimed.Select(x => x.Id).Should().Contain(available.Id).And.NotContain(locked.Id);
            await transaction.RollbackAsync(ct);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }
}
