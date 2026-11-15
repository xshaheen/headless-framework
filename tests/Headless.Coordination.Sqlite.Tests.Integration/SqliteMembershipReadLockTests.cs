// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Headless.Sql;
using Headless.Sql.Sqlite;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<SqliteMembershipFixture>]
public sealed class SqliteMembershipReadLockTests(SqliteMembershipFixture fixture) : TestBase
{
    // A read that queued for the write lock would wait for the connection's 30-second busy timeout; one that takes only
    // a shared lock returns in milliseconds, so this window separates the two without timing the read itself.
    private static readonly TimeSpan _BlockedObservationWindow = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task should_read_liveness_while_another_connection_holds_the_write_lock()
    {
        await using var node = await fixture.CreateNodeAsync(
            $"cluster-{Guid.NewGuid():N}",
            "node-a",
            MembershipLostBehavior.StopMembershipOnly,
            schema: null,
            // Long thresholds keep the node alive however slowly the host runs the test.
            options =>
            {
                options.HeartbeatInterval = TimeSpan.FromSeconds(5);
                options.SuspicionThreshold = TimeSpan.FromMinutes(1);
                options.DeadThreshold = TimeSpan.FromMinutes(2);
                options.DeadRetentionWindow = TimeSpan.FromMinutes(2);
            },
            AbortToken
        );
        var identity = await node.Membership.RegisterAsync(AbortToken);
        var store = node.Services.GetRequiredService<IMembershipStore>();

        // The first snapshot runs the throttled retention prune, a write; the reads below then run alone.
        (await store.ReadLivenessAsync(AbortToken))
            .Should()
            .ContainSingle();

        await using var writer = new SqliteConnection(fixture.ConnectionString);
        await writer.OpenAsync(AbortToken);
        await using var transaction = (SqliteTransaction)await writer.BeginTransactionAsync(AbortToken);
        await using (var command = writer.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {SqliteDialect.Instance.Qualify(HeadlessStorageDefaults.Schema, "coordination_liveness")} SET last_beat = last_beat";
            (await command.ExecuteNonQueryAsync(AbortToken)).Should().BePositive();
        }

        var snapshot = await _WithinWindowAsync(store.ReadLivenessAsync(AbortToken).AsTask());
        var state = await _WithinWindowAsync(store.ReadNodeLivenessAsync(identity, AbortToken).AsTask());
        var live = await _WithinWindowAsync(store.ReadLiveNodesAsync(AbortToken).AsTask());

        snapshot.Should().ContainSingle(x => x.Identity == identity && x.State == NodeLivenessState.Alive);
        state.Should().Be(NodeLivenessState.Alive);
        live.Should().Equal([identity]);

        await transaction.RollbackAsync(AbortToken);
    }

    private static async Task<T> _WithinWindowAsync<T>(Task<T> read)
    {
        var first = await Task.WhenAny(read, Task.Delay(_BlockedObservationWindow, AbortToken));

        first.Should().BeSameAs(read, "a liveness read takes only a shared lock, never the write lock");

        return await read;
    }
}
