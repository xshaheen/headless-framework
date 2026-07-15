// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// SQL Server binding of the cross-provider <see cref="DeadOwnerReclaimConformanceTests"/>. A second concurrent
/// bridge host shares the same database through the connection string, so it uses its own storage instance.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerDeadOwnerReclaimConformanceTests(SqlServerTestFixture fixture)
    : DeadOwnerReclaimConformanceTests
{
    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.UseSqlServer(fixture.ConnectionString);
    }

    protected override async Task ResetStorageAsync(IDataStorage storage)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "TRUNCATE TABLE messaging.Published; TRUNCATE TABLE messaging.Received;",
                cancellationToken: AbortToken
            )
        );
    }

    [Fact]
    public override Task should_reclaim_dead_owner_published_and_received_rows()
    {
        return base.should_reclaim_dead_owner_published_and_received_rows();
    }

    [Fact]
    public override Task should_not_reclaim_suspected_owner_rows()
    {
        return base.should_not_reclaim_suspected_owner_rows();
    }

    [Fact]
    public override Task should_reclaim_once_when_surfaced_by_both_event_and_reconcile()
    {
        return base.should_reclaim_once_when_surfaced_by_both_event_and_reconcile();
    }

    [Fact]
    public override Task should_fence_a_restarted_incarnation()
    {
        return base.should_fence_a_restarted_incarnation();
    }

    [Fact]
    public override Task should_recover_aged_out_owner_via_lease_floor()
    {
        return base.should_recover_aged_out_owner_via_lease_floor();
    }

    [Fact]
    public override Task should_reclaim_once_under_two_concurrent_bridges()
    {
        return base.should_reclaim_once_under_two_concurrent_bridges();
    }
}
