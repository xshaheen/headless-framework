// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Occurrence rows created by the shared EF materialization path must be keyed with the same generator the SQL Server
/// claim strategy resolves — the comb, not UUIDv7. SQL Server sorts <c>uniqueidentifier</c> by the trailing bytes,
/// which are exactly the bytes UUIDv7 fills with randomness, so a UUIDv7 id lands at an arbitrary point in the
/// clustered primary key and fragments it. Nothing about the row's own outcome shows this, which is why it went
/// unnoticed when ordinary occurrence creation moved from the native strategy into the shared provider.
/// </summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerOccurrenceIdOrderingTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    // Enough ids that an accidental pass is implausible: a UUIDv7 sequence would have to land in database order by
    // chance (1 in 8!).
    private const int _OccurrenceCount = 8;

    [Fact]
    public async Task materialized_occurrence_ids_are_sequential_in_sql_server_ordering()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("occurrence-id-ordering");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var cronId = Guid.NewGuid();

        // Far enough in the past that every advance below still leaves the projection due (each one moves NextDueUtc
        // forward by a minute).
        await fixture.SeedCronJobAsync(
            cronId,
            "occurrence-id-ordering",
            "* * * * *",
            NodeDeathPolicy.Retry,
            ct,
            reconciledThroughOffsetSeconds: -900,
            nextDueOffsetSeconds: -600
        );

        var createdIds = new List<Guid>(_OccurrenceCount);

        for (var i = 0; i < _OccurrenceCount; i++)
        {
            var position = await fixture.ReadCronSchedulePositionAsync(cronId, ct);

            var result = await persistence.MaterializeCronScheduleOccurrenceAsync(
                new CronScheduleMaterialization
                {
                    Advance = new CronScheduleAdvance
                    {
                        CronJobId = cronId,
                        ObservedReconciledThroughUtc = position.ReconciledThroughUtc,
                        ExpectedScheduleRevision = 0,
                        ReconciledThroughUtc = position.NextDueUtc,
                        NextDueUtc = position.NextDueUtc.AddMinutes(1),
                        RequireProjectionDue = true,
                    },
                    ExecutionTimeUtc = position.NextDueUtc,
                },
                ct
            );

            result.Outcome.Should().Be(CronScheduleMaterializationOutcome.OccurrenceCreated);
            result.OccurrenceId.Should().NotBeNull();
            createdIds.Add(result.OccurrenceId!.Value);
        }

        createdIds.Should().OnlyHaveUniqueItems();

        var databaseOrderedIds = await _ReadOccurrenceIdsInDatabaseOrderAsync(ct);

        databaseOrderedIds
            .Should()
            .Equal(
                createdIds,
                "SQL Server's own uniqueidentifier ordering must match creation order, so each new occurrence appends "
                    + "to the right edge of the clustered primary key instead of fragmenting it"
            );

        // The generator the native SQL Server claim strategy injects by key. Its next value must sort after every id
        // the materialization path just wrote — they can only share one monotonic sequence if they are the same
        // generator.
        var nativeGenerator = host.Services.GetRequiredKeyedService<IGuidGenerator>(SequentialGuidType.SqlServer);
        var laterNativeId = nativeGenerator.Create();

        (await _CountOccurrencesSortingAfterAsync(laterNativeId, ct))
            .Should()
            .Be(
                0,
                "an id created later by the native strategy's generator must sort after every materialized occurrence id"
            );
    }

    private async Task<List<Guid>> _ReadOccurrenceIdsInDatabaseOrderAsync(CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Id\" FROM {fixture.QualifiedCronJobOccurrencesTable} ORDER BY \"Id\";";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<int> _CountOccurrencesSortingAfterAsync(Guid reference, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {fixture.QualifiedCronJobOccurrencesTable} WHERE \"Id\" > @reference;";
        _AddParameter(command, "@reference", reference);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static void _AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
