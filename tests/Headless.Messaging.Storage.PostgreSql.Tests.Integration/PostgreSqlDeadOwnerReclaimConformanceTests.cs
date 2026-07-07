// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Npgsql;

namespace Tests;

/// <summary>
/// PostgreSQL binding of the cross-provider <see cref="DeadOwnerReclaimConformanceTests"/>. A second concurrent
/// bridge host shares the same database through the connection string, so it uses its own storage instance.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlDeadOwnerReclaimConformanceTests(PostgreSqlTestFixture fixture)
    : DeadOwnerReclaimConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup) =>
        setup.UsePostgreSql(fixture.ConnectionString);

    protected override async Task ResetStorageAsync(IDataStorage storage)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                TRUNCATE TABLE messaging.published;
                TRUNCATE TABLE messaging.received;
                """,
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public override Task should_reclaim_dead_owner_published_and_received_rows() =>
        base.should_reclaim_dead_owner_published_and_received_rows();

    [Fact]
    public override Task should_not_reclaim_suspected_owner_rows() => base.should_not_reclaim_suspected_owner_rows();

    [Fact]
    public override Task should_reclaim_once_when_surfaced_by_both_event_and_reconcile() =>
        base.should_reclaim_once_when_surfaced_by_both_event_and_reconcile();

    [Fact]
    public override Task should_fence_a_restarted_incarnation() => base.should_fence_a_restarted_incarnation();

    [Fact]
    public override Task should_recover_aged_out_owner_via_lease_floor() =>
        base.should_recover_aged_out_owner_via_lease_floor();

    [Fact]
    public override Task should_reclaim_once_under_two_concurrent_bridges() =>
        base.should_reclaim_once_under_two_concurrent_bridges();
}
