// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Headless.Sql;
using Headless.Testing.Tests;
using Xunit;

namespace Tests;

/// <summary>
/// The engine-neutral contract of a lease stored on the consumer's own row, run against a real database. A provider
/// leaf derives from it, supplies its <see cref="IFencedRowsFixture" />, and overrides every test with <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// Each test inserts its own rows under fresh keys and claims only rows tagged with its own status, so tests never
/// share a row. Expiry is produced by moving a row's stored expiry into the database's past rather than by waiting,
/// except where the test is about the clock a statement reads after it takes the row lock.
/// </remarks>
public abstract class FencedRowsConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IFencedRowsFixture
{
    /// <summary>A duration no test outlives, so a lease granted with it stays live unless a test ages it.</summary>
    protected static readonly TimeSpan LongDuration = TimeSpan.FromMinutes(5);

    /// <summary>A lease the database clock lets run out while a test holds the row lock.</summary>
    protected static readonly TimeSpan ShortDuration = TimeSpan.FromSeconds(1);

    private static readonly SqlColumnType _Status = SqlColumnType.Text(200);

    private readonly FencedRowTable<Guid> _table = _Create(fixture, IFencedRowsFixture.WorkItemsTable, null);

    private readonly FencedRowTable<Guid> _sequenced = _Create(
        fixture,
        IFencedRowsFixture.WorkItemsTable,
        IFencedRowsFixture.GenerationSequence
    );

    protected TFixture Fixture { get; } = fixture;

    private ISqlDialect Dialect => Fixture.Dialect;

    private string WorkItems => Dialect.Qualify(Fixture.Schema, IFencedRowsFixture.WorkItemsTable);

    #region Grant

    public virtual async Task should_grant_generation_one_to_a_fresh_row_and_store_the_lease_on_it()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();

        var granted = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );

        granted.Should().NotBeNull();
        granted!.Status.Should().Be(LeaseGrantStatus.Granted);
        granted.Row.Should().Be(new FencedRow<Guid>(_table.Name, id, 1));
        granted.TakeoverCount.Should().Be(0);

        var stored = await ReadAsync(id);
        stored.Status.Should().Be("pending", "a grant writes the lease columns and nothing else");
        stored.Lease.Should().Be(new RowLeaseSnapshot(1, RowLeaseState.Active, granted.ExpiresAt, "node-a", 0));
    }

    public virtual async Task should_return_null_and_insert_nothing_when_no_row_has_the_key()
    {
        var missing = Guid.NewGuid();
        await using var connection = Fixture.CreateConnection();

        var granted = await FencedRows.GrantAsync(_table, connection, null, missing, LongDuration, new(), AbortToken);

        granted.Should().BeNull();
        (await TryReadAsync(missing)).Should().BeNull("the helper never inserts the consumer's row");
    }

    public virtual async Task should_hand_back_the_live_attempt_unchanged_when_the_same_holder_grants_again()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var first = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );

        var again = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );

        again!.Status.Should().Be(LeaseGrantStatus.Granted, "a grant retried after a lost response is idempotent");
        again.Row.Should().Be(first!.Row);
        again.ExpiresAt.Should().Be(first.ExpiresAt, "the retry writes nothing, so the expiry is the first grant's");
        (await ReadAsync(id))
            .Lease.Should()
            .Be(new RowLeaseSnapshot(1, RowLeaseState.Active, first.ExpiresAt, "node-a", 0));
    }

    public virtual async Task should_report_the_live_holder_and_change_nothing_when_the_row_is_held()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var first = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );
        var before = await ReadAsync(id);

        var other = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-b"),
            AbortToken
        );
        var differentCase = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("NODE-A"),
            AbortToken
        );
        var anonymous = await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken);

        foreach (var held in (FencedRowGrantResult<Guid>?[])[other, differentCase, anonymous])
        {
            held!.Status.Should().Be(LeaseGrantStatus.Held);
            held.Row.Should().BeNull();
            held.HolderGeneration.Should().Be(1);
            held.ExpiresAt.Should().Be(first!.ExpiresAt);
        }

        (await ReadAsync(id)).Should().Be(before);
    }

    public virtual async Task should_take_over_an_expired_attempt_count_it_and_refuse_the_older_generation()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var first = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );
        await ExpireAsync(id);

        var second = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-b"),
            AbortToken
        );

        second!.Status.Should().Be(LeaseGrantStatus.Takeover);
        second.Row.Should().Be(new FencedRow<Guid>(_table.Name, id, 2));
        second.PreviousGeneration.Should().Be(1);
        second.TakeoverCount.Should().Be(1);
        (await ReadAsync(id))
            .Lease.Should()
            .Be(new RowLeaseSnapshot(2, RowLeaseState.Active, second.ExpiresAt, "node-b", 1));

        (await FencedRows.SettleAsync(_table, connection, null, first!.Row!, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Stale);
    }

    public virtual async Task should_report_an_expired_attempt_and_change_nothing_when_the_grant_waits_for_the_sweep()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, Holder("node-a"), AbortToken);
        await ExpireAsync(id);
        var before = await ReadAsync(id);

        var waiting = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            id,
            LongDuration,
            new LeaseGrantOptions { Holder = "node-b", Takeover = LeaseTakeover.AfterSweep },
            AbortToken
        );

        waiting!.Status.Should().Be(LeaseGrantStatus.Expired);
        waiting.Row.Should().BeNull();
        waiting.HolderGeneration.Should().Be(1);
        (await ReadAsync(id)).Should().Be(before);
    }

    #endregion

    #region Claim

    public virtual async Task should_claim_only_grantable_rows_each_under_its_own_next_generation()
    {
        var tag = Tag();
        var held = await InsertAsync(tag);
        var settled = await InsertAsync(tag);
        var fresh = await InsertAsync(tag);
        await using var connection = Fixture.CreateConnection();
        await FencedRows.GrantAsync(_table, connection, null, held, LongDuration, new(), AbortToken);
        var first = await FencedRows.GrantAsync(_table, connection, null, settled, LongDuration, new(), AbortToken);
        await FencedRows.SettleAsync(_table, connection, null, first!.Row!, cancellationToken: AbortToken);

        var claimed = await FencedRows.ClaimAsync(_table, connection, null, Claim(tag, 10, "node-c"), AbortToken);

        claimed
            .Should()
            .BeEquivalentTo([new FencedRow<Guid>(_table.Name, settled, 2), new FencedRow<Guid>(_table.Name, fresh, 1)]);

        foreach (var row in claimed)
        {
            var stored = await ReadAsync(row.Key);
            stored.Lease.Generation.Should().Be(row.Generation);
            stored.Lease.State.Should().Be(RowLeaseState.Active);
            stored.Lease.Holder.Should().Be("node-c");
        }

        (await FencedRows.ClaimAsync(_table, connection, null, Claim(tag, 10, "node-c"), AbortToken))
            .Should()
            .BeEmpty("every tagged row is now held by a live attempt");
    }

    public virtual async Task should_skip_a_row_another_transaction_claimed_without_waiting_for_it()
    {
        Assert.SkipUnless(Fixture.ClaimSkipsLockedRows, "The engine serializes claimers instead of skipping rows.");
        var tag = Tag();
        var ids = (Guid[])[await InsertAsync(tag), await InsertAsync(tag)];
        await using var holder = await OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(AbortToken);
        var first = await FencedRows.ClaimAsync(_table, holder, transaction, Claim(tag, 1, "node-a"), AbortToken);

        await using var other = Fixture.CreateConnection();
        var second = await Task.Run(
                async () => await FencedRows.ClaimAsync(_table, other, null, Claim(tag, 2, "node-b"), AbortToken),
                AbortToken
            )
            .WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        first.Should().ContainSingle();
        second.Should().ContainSingle("the row the open transaction claimed is skipped, not waited on");
        second[0].Key.Should().NotBe(first[0].Key);
        ids.Should().Contain([first[0].Key, second[0].Key]);
        await transaction.CommitAsync(AbortToken);
    }

    public virtual async Task should_claim_only_what_an_open_claim_left_once_it_commits()
    {
        var tag = Tag();
        await InsertAsync(tag);
        await InsertAsync(tag);
        await using var holder = await OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(AbortToken);
        var first = await FencedRows.ClaimAsync(_table, holder, transaction, Claim(tag, 1, "node-a"), AbortToken);

        await using var other = Fixture.CreateConnection();
        var pending = Task.Run(
            async () => await FencedRows.ClaimAsync(_table, other, null, Claim(tag, 2, "node-b"), AbortToken),
            AbortToken
        );

        if (!Fixture.ClaimSkipsLockedRows)
        {
            // Without row locks a claim takes the database write lock, so the second claimer waits for the first.
            var done = await Task.WhenAny(
                pending,
                Task.Delay(LeasesFixtureExtensions.BlockedObservationWindow, AbortToken)
            );
            done.Should().NotBeSameAs(pending, "the second claim waits for the open claim's write lock");
        }

        await transaction.CommitAsync(AbortToken);
        var second = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        first.Should().ContainSingle();
        second.Should().ContainSingle("the committed claim holds its row, so only the other one is left");
        second[0].Key.Should().NotBe(first[0].Key);
        (await ReadAsync(first[0].Key)).Lease.Holder.Should().Be("node-a");
        (await ReadAsync(second[0].Key)).Lease.Holder.Should().Be("node-b");
    }

    #endregion

    #region Renew, settle, release, revoke

    public virtual async Task should_renew_a_live_generation_and_report_why_any_other_is_refused()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var first = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!;

        var renewed = await FencedRows.RenewAsync(_table, connection, null, first.Row!, LongDuration * 2, AbortToken);

        renewed.Status.Should().Be(LeaseRenewalStatus.Renewed);
        renewed.ExpiresAt.Should().BeAfter(first.ExpiresAt);
        (await ReadAsync(id)).Lease.ExpiresAt.Should().Be(renewed.ExpiresAt);

        await ExpireAsync(id, LongDuration * 3);
        var expiredAt = (await ReadAsync(id)).Lease.ExpiresAt;
        var expired = await FencedRows.RenewAsync(_table, connection, null, first.Row!, LongDuration, AbortToken);
        expired.Status.Should().Be(LeaseRenewalStatus.Expired);
        expired.ExpiresAt.Should().Be(expiredAt, "an expired renewal reports the unextended expiry");
        (await ReadAsync(id)).Lease.ExpiresAt.Should().Be(expiredAt);

        var second = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!;
        (await FencedRows.RenewAsync(_table, connection, null, first.Row!, LongDuration, AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Stale);

        await FencedRows.SettleAsync(_table, connection, null, second.Row!, cancellationToken: AbortToken);
        (await FencedRows.RenewAsync(_table, connection, null, second.Row!, LongDuration, AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Settled);
    }

    public virtual async Task should_settle_once_and_report_the_settlement_to_a_retry_or_a_release()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var granted = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!;
        var row = granted.Row!;

        (await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled);
        var settled = await ReadAsync(id);

        (await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled, "a retried settlement reports success");
        (await FencedRows.ReleaseAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled, "a release after a settlement reports that the attempt settled");

        settled.Lease.Should().Be(new RowLeaseSnapshot(1, RowLeaseState.Settled, granted.ExpiresAt, null, 0));
        (await ReadAsync(id)).Should().Be(settled, "the retries write nothing");
    }

    public virtual async Task should_release_once_and_let_the_next_grant_proceed_at_once()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;

        (await FencedRows.ReleaseAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Released);
        (await FencedRows.ReleaseAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Released);
        (await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Released);
        (await ReadAsync(id)).Lease.State.Should().Be(RowLeaseState.Released);

        var next = await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken);

        next!.Status.Should().Be(LeaseGrantStatus.Granted, "a released row is not a takeover");
        next.Row!.Generation.Should().Be(2);
    }

    public virtual async Task should_refuse_a_settlement_of_an_expired_or_replaced_generation()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var first = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;
        await ExpireAsync(id);

        (await FencedRows.SettleAsync(_table, connection, null, first, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Expired);
        (await FencedRows.ReleaseAsync(_table, connection, null, first, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Expired);
        (await ReadAsync(id)).Lease.State.Should().Be(RowLeaseState.Active, "a refused settlement writes nothing");

        await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken);

        (await FencedRows.SettleAsync(_table, connection, null, first, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Stale);
        (await FencedRows.ReleaseAsync(_table, connection, null, first, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Stale);
    }

    public virtual async Task should_revoke_the_active_attempt_live_or_expired_and_report_revoked_to_its_verbs()
    {
        var live = await InsertAsync("pending");
        var expired = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (
            await FencedRows.GrantAsync(_table, connection, null, live, LongDuration, Holder("node-a"), AbortToken)
        )!.Row!;
        await FencedRows.GrantAsync(_table, connection, null, expired, LongDuration, new(), AbortToken);
        await ExpireAsync(expired);

        (await FencedRows.RevokeAsync(_table, connection, null, live, AbortToken)).Should().Be(row);
        (await FencedRows.RevokeAsync(_table, connection, null, expired, AbortToken))
            .Should()
            .Be(new FencedRow<Guid>(_table.Name, expired, 1));

        var stored = await ReadAsync(live);
        stored.Lease.State.Should().Be(RowLeaseState.Revoked);
        stored.Lease.Holder.Should().Be("node-a", "a revoked attempt keeps its holder for the operator");

        (await FencedRows.RevokeAsync(_table, connection, null, live, AbortToken))
            .Should()
            .BeNull("nothing is active any more");
        (await FencedRows.RevokeAsync(_table, connection, null, Guid.NewGuid(), AbortToken)).Should().BeNull();
        (await FencedRows.RenewAsync(_table, connection, null, row, LongDuration, AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Revoked);
        (await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Revoked);
        (await FencedRows.ReleaseAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Revoked);
        (await FencedRows.GetStatusAsync(_table, connection, null, row, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Revoked);
        (await ReadAsync(live)).Should().Be(stored, "every verb of the revoked generation writes nothing");

        var next = await FencedRows.GrantAsync(_table, connection, null, live, LongDuration, new(), AbortToken);
        next!.Status.Should().Be(LeaseGrantStatus.Granted);
        next.Row!.Generation.Should().Be(2);
        next.TakeoverCount.Should().Be(0);
    }

    public virtual async Task should_release_tokens_of_different_generations_in_one_call_each_judged_on_its_own()
    {
        var x = await InsertAsync("pending");
        var y = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var xRow = (await FencedRows.GrantAsync(_table, connection, null, x, LongDuration, new(), AbortToken))!.Row!;
        var yOld = (await FencedRows.GrantAsync(_table, connection, null, y, LongDuration, new(), AbortToken))!.Row!;
        await FencedRows.ReleaseAsync(_table, connection, null, yOld, cancellationToken: AbortToken);
        var yRow = (await FencedRows.GrantAsync(_table, connection, null, y, LongDuration, new(), AbortToken))!.Row!;

        var statuses = await FencedRows.ReleaseAsync(_table, connection, null, [xRow, yRow, yOld], AbortToken);

        xRow.Generation.Should().Be(1);
        yRow.Generation.Should().Be(2);
        statuses
            .Should()
            .Equal(LeaseSettlementStatus.Released, LeaseSettlementStatus.Released, LeaseSettlementStatus.Stale);
        (await ReadAsync(x))
            .Lease.Should()
            .Be((await ReadAsync(x)).Lease with { Generation = 1, State = RowLeaseState.Released });
        (await ReadAsync(y))
            .Lease.Should()
            .Be((await ReadAsync(y)).Lease with { Generation = 2, State = RowLeaseState.Released });
    }

    #endregion

    #region Composed writes

    public virtual async Task should_apply_a_composed_settle_and_refuse_it_after_a_takeover()
    {
        var mine = await InsertAsync("pending");
        var lost = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, mine, LongDuration, new(), AbortToken))!.Row!;
        var old = (
            await FencedRows.GrantAsync(_table, connection, null, lost, LongDuration, Holder("node-a"), AbortToken)
        )!.Row!;
        await ExpireAsync(lost);
        await FencedRows.GrantAsync(_table, connection, null, lost, LongDuration, Holder("node-b"), AbortToken);

        (await FencedRows.SettleAsync(_table, connection, null, row, SetStatus("done"), AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled);
        (await FencedRows.SettleAsync(_table, connection, null, old, SetStatus("done"), AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Stale);

        var settled = await ReadAsync(mine);
        settled.Status.Should().Be("done", "the consumer's assignment commits with the settlement");
        settled.Lease.State.Should().Be(RowLeaseState.Settled);

        var taken = await ReadAsync(lost);
        taken.Status.Should().Be("pending", "the replaced attempt's write is refused with its settlement");
        taken.Lease.Should().Be(taken.Lease with { Generation = 2, State = RowLeaseState.Active, Holder = "node-b" });
    }

    public virtual async Task should_write_while_current_and_tell_a_refused_condition_from_a_lost_lease()
    {
        var id = await InsertAsync("pending");
        var missing = new FencedRow<Guid>(_table.Name, Guid.NewGuid(), 1);
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;
        var write = new FencedRowWrite(
            Set: "status = @Next",
            Condition: "status = @Expected",
            Returning: ["status"],
            Bind: command =>
            {
                Dialect.AddParameter(command, "Next", _Status, "progress");
                Dialect.AddParameter(command, "Expected", _Status, "pending");
            }
        );

        var applied = await FencedRows.WriteAsync(_table, connection, null, row, write, _ReadStatusAsync, AbortToken);
        var refused = await FencedRows.WriteAsync(_table, connection, null, row, write, _ReadStatusAsync, AbortToken);
        await ExpireAsync(id);
        var expired = await FencedRows.WriteAsync(_table, connection, null, row, write, _ReadStatusAsync, AbortToken);
        var absent = await FencedRows.WriteAsync(
            _table,
            connection,
            null,
            missing,
            write,
            _ReadStatusAsync,
            AbortToken
        );

        applied.IsAccepted.Should().BeTrue();
        applied.Match<string?>(accepted: (status, _) => status, rejected: _ => null).Should().Be("progress");
        applied.Before!.Status.Should().Be(LeaseFenceStatus.Current);

        refused.IsAccepted.Should().BeFalse();
        refused
            .Before!.Status.Should()
            .Be(LeaseFenceStatus.Current, "the lease is live; the consumer's condition failed");

        expired.IsAccepted.Should().BeFalse();
        expired.Before!.Status.Should().Be(LeaseFenceStatus.Expired);

        absent.IsAccepted.Should().BeFalse();
        absent.Before.Should().BeNull("no row has the key");

        var stored = await ReadAsync(id);
        stored.Status.Should().Be("progress");
        stored.Lease.State.Should().Be(RowLeaseState.Active, "a fenced write keeps the lease active");
    }

    public virtual async Task should_refuse_a_composed_write_whose_row_lock_was_held_past_the_expiry()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, ShortDuration, new(), AbortToken))!.Row!;
        await using var holder = await OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync(AbortToken);

        (await FencedRows.FenceAsync(_table, holder, transaction, row, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Current);

        await using var writer = Fixture.CreateConnection();
        var pending = Task.Run(
            async () =>
                await FencedRows.WriteAsync(
                    _table,
                    writer,
                    null,
                    row,
                    SetStatus("progress"),
                    static (_, _) => ValueTask.FromResult(true),
                    AbortToken
                ),
            AbortToken
        );
        var first = await Task.WhenAny(
            pending,
            Task.Delay(LeasesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );
        first.Should().NotBeSameAs(pending, "the write waits on the row the fence holds");

        // The lease runs out while the write waits: a clock read before the row lock would still call it live.
        await Task.Delay(ShortDuration, AbortToken);
        await transaction.CommitAsync(AbortToken);
        var written = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        written.IsAccepted.Should().BeFalse();
        written.Before!.Status.Should().Be(LeaseFenceStatus.Expired);
        (await ReadAsync(id)).Status.Should().Be("pending");
    }

    public virtual async Task should_grant_a_subtree_the_roots_deadline_with_a_generation_per_child()
    {
        var root = await InsertAsync("pending");
        var reclaimed = await InsertAsync("pending", parent: root);
        var fresh = await InsertAsync("pending", parent: root);
        await using var connection = Fixture.CreateConnection();
        var rootGrant = (await FencedRows.GrantAsync(_table, connection, null, root, LongDuration, new(), AbortToken))!;
        var earlier = (
            await FencedRows.GrantAsync(_table, connection, null, reclaimed, LongDuration, new(), AbortToken)
        )!;
        await FencedRows.ReleaseAsync(_table, connection, null, earlier.Row!, cancellationToken: AbortToken);

        var scope = FencedRows.GrantScope(_table, "Child");
        var sql = Dialect.Render(
            new SqlClaimNext(
                WorkItems,
                [Dialect.Quote("id")],
                "parent_id = @Root",
                [Dialect.Quote("id")],
                scope.Assignments($"(SELECT root.lease_expires_at FROM {WorkItems} root WHERE root.id = @Root)"),
                scope.Returning,
                "Batch"
            )
        );
        var children = new List<(Guid Id, long Generation, DateTimeOffset ExpiresAt)>();

        await using (var claimer = await OpenAsync())
        await using (var transaction = await claimer.BeginTransactionAsync(AbortToken))
        {
            await using (var command = claimer.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                scope.Bind(command, LongDuration, "node-s");
                Dialect.AddParameter(command, "Root", SqlColumnType.Guid, root);
                Dialect.AddParameter(command, "Batch", SqlColumnType.Int32, 10);
                await using var reader = await command.ExecuteReaderAsync(AbortToken);

                do
                {
                    while (await reader.ReadAsync(AbortToken))
                    {
                        children.Add(
                            (
                                await reader.GetFieldValueAsync<Guid>(0, AbortToken),
                                reader.GetInt64(1),
                                await reader.GetFieldValueAsync<DateTimeOffset>(2, AbortToken)
                            )
                        );
                    }
                } while (await reader.NextResultAsync(AbortToken));
            }

            await transaction.CommitAsync(AbortToken);
        }

        children.Should().HaveCount(2);
        children
            .Should()
            .OnlyContain(child => child.ExpiresAt == rootGrant.ExpiresAt, "the subtree expires with its root");
        children.Single(child => child.Id == reclaimed).Generation.Should().Be(2);
        children.Single(child => child.Id == fresh).Generation.Should().Be(1);
        (await ReadAsync(fresh)).Lease.Holder.Should().Be("node-s");

        // Each child's token fences its own row only: a sibling's generation is not current on it.
        (
            await FencedRows.GetStatusAsync(
                _table,
                connection,
                null,
                new FencedRow<Guid>(_table.Name, fresh, 2),
                AbortToken
            )
        )
            .Should()
            .Be(LeaseFenceStatus.Stale);
        (
            await FencedRows.SettleAsync(
                _table,
                connection,
                null,
                new FencedRow<Guid>(_table.Name, reclaimed, 2),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .Be(LeaseSettlementStatus.Settled);
    }

    #endregion

    #region Fence, status, sweep

    public virtual async Task should_fence_inside_the_callers_transaction_and_judge_every_other_generation()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;
        await using var fencer = await OpenAsync();

        await using (var transaction = await fencer.BeginTransactionAsync(AbortToken))
        {
            (await FencedRows.FenceAsync(_table, fencer, transaction, row, AbortToken))
                .Should()
                .Be(LeaseFenceStatus.Current);
            (await FencedRows.FenceAsync(_table, fencer, transaction, row with { Generation = 2 }, AbortToken))
                .Should()
                .Be(LeaseFenceStatus.Stale);
            await transaction.CommitAsync(AbortToken);
        }

        await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken);

        await using (var transaction = await fencer.BeginTransactionAsync(AbortToken))
        {
            (await FencedRows.FenceAsync(_table, fencer, transaction, row, AbortToken))
                .Should()
                .Be(LeaseFenceStatus.Settled);
            await transaction.CommitAsync(AbortToken);
        }
    }

    public virtual async Task should_read_each_status_by_the_database_clock_without_a_transaction_or_a_write()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;

        (await FencedRows.GetStatusAsync(_table, connection, null, row, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Current);
        (await FencedRows.GetStatusAsync(_table, connection, null, row with { Generation = 2 }, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Stale);
        (await FencedRows.GetStatusAsync(_table, connection, null, row with { Key = Guid.NewGuid() }, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Stale, "no row has the key");

        await ExpireAsync(id);
        var expired = await ReadAsync(id);
        (await FencedRows.GetStatusAsync(_table, connection, null, row, AbortToken))
            .Should()
            .Be(LeaseFenceStatus.Expired);
        (await ReadAsync(id)).Should().Be(expired, "a status read changes nothing");
        connection.State.Should().Be(System.Data.ConnectionState.Closed, "the helper closes a connection it opened");
    }

    public virtual async Task should_abandon_only_expired_active_attempts_and_count_each_as_a_takeover()
    {
        var expired = await InsertAsync("pending");
        var live = await InsertAsync("pending");
        var settled = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (
            await FencedRows.GrantAsync(_table, connection, null, expired, LongDuration, new(), AbortToken)
        )!.Row!;
        await FencedRows.GrantAsync(_table, connection, null, live, LongDuration, new(), AbortToken);
        var done = (
            await FencedRows.GrantAsync(_table, connection, null, settled, LongDuration, new(), AbortToken)
        )!.Row!;
        await FencedRows.SettleAsync(_table, connection, null, done, cancellationToken: AbortToken);
        await ExpireAsync(expired);
        await ExpireAsync(settled);

        // Earlier tests leave expired rows behind, so the sweep runs until a batch comes back short.
        const int batch = 50;
        int swept;

        do
        {
            swept = await FencedRows.AbandonExpiredAsync(_table, connection, null, batch, AbortToken);
        } while (swept == batch);

        var abandoned = await ReadAsync(expired);
        abandoned.Lease.State.Should().Be(RowLeaseState.Abandoned);
        abandoned.Lease.TakeoverCount.Should().Be(1);
        (await ReadAsync(live)).Lease.State.Should().Be(RowLeaseState.Active);
        (await ReadAsync(settled)).Lease.State.Should().Be(RowLeaseState.Settled);
        (await FencedRows.SettleAsync(_table, connection, null, row, cancellationToken: AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Abandoned);

        var next = await FencedRows.GrantAsync(
            _table,
            connection,
            null,
            expired,
            LongDuration,
            new LeaseGrantOptions { Takeover = LeaseTakeover.AfterSweep },
            AbortToken
        );
        next!.Status.Should().Be(LeaseGrantStatus.Granted, "a grant waiting for the sweep proceeds once it ran");
        next.Row!.Generation.Should().Be(2);
        next.TakeoverCount.Should().Be(1, "the sweep's takeover still counts until the next attempt settles");
    }

    #endregion

    #region Guards and generation source

    public virtual async Task should_throw_for_a_token_another_table_issued_before_any_statement()
    {
        var id = await InsertAsync("pending");
        var other = _Create(Fixture, "other_items", null);
        await using var connection = Fixture.CreateConnection();
        await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken);
        var foreign = new FencedRow<Guid>(other.Name, id, 1);

        var settle = async () =>
            await FencedRows.SettleAsync(_table, connection, null, foreign, cancellationToken: AbortToken);
        var batch = async () => await FencedRows.ReleaseAsync(_table, connection, null, [foreign], AbortToken);

        await settle.Should().ThrowAsync<ArgumentException>();
        await batch.Should().ThrowAsync<ArgumentException>();
        (await ReadAsync(id)).Lease.State.Should().Be(RowLeaseState.Active);
    }

    public virtual async Task should_throw_when_a_write_binds_a_reserved_parameter_or_a_settle_takes_a_condition()
    {
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();
        var row = (await FencedRows.GrantAsync(_table, connection, null, id, LongDuration, new(), AbortToken))!.Row!;
        var reserved = new FencedRowWrite(
            Set: "status = @LeaseStatus",
            Bind: command => Dialect.AddParameter(command, "LeaseStatus", _Status, "done")
        );
        var conditional = SetStatus("done") with { Condition = "status = 'pending'" };

        var bindReserved = async () =>
            await FencedRows.SettleAsync(_table, connection, null, row, reserved, AbortToken);
        var settleConditional = async () =>
            await FencedRows.SettleAsync(_table, connection, null, row, conditional, AbortToken);

        var dialectVariable = new FencedRowWrite(
            Set: "status = @now",
            Bind: command => Dialect.AddParameter(command, "now", _Status, "done")
        );
        var bindDialectVariable = async () =>
            await FencedRows.SettleAsync(_table, connection, null, row, dialectVariable, AbortToken);

        await bindReserved.Should().ThrowAsync<ArgumentException>();
        await bindDialectVariable.Should().ThrowAsync<ArgumentException>();
        await settleConditional.Should().ThrowAsync<ArgumentException>();
        var stored = await ReadAsync(id);
        stored.Status.Should().Be("pending");
        stored.Lease.State.Should().Be(RowLeaseState.Active, "the refused writes rolled back with nothing applied");
    }

    public virtual async Task should_draw_store_wide_increasing_generations_from_the_sequence()
    {
        var tag = Tag();
        var a = await InsertAsync("pending");
        var b = await InsertAsync("pending");
        await InsertAsync(tag);
        await InsertAsync(tag);
        await using var connection = Fixture.CreateConnection();

        var first = (
            await FencedRows.GrantAsync(_sequenced, connection, null, a, LongDuration, new(), AbortToken)
        )!.Row!;
        var second = (
            await FencedRows.GrantAsync(_sequenced, connection, null, b, LongDuration, new(), AbortToken)
        )!.Row!;
        await FencedRows.ReleaseAsync(_sequenced, connection, null, first, cancellationToken: AbortToken);
        var third = (
            await FencedRows.GrantAsync(_sequenced, connection, null, a, LongDuration, new(), AbortToken)
        )!.Row!;
        var claimed = await FencedRows.ClaimAsync(_sequenced, connection, null, Claim(tag, 10, null), AbortToken);

        second.Generation.Should().BeGreaterThan(first.Generation, "another row's grant still draws a higher value");
        third.Generation.Should().BeGreaterThan(second.Generation);
        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(row => row.Generation > third.Generation);
        (await ReadAsync(a)).Lease.Generation.Should().Be(third.Generation);
    }

    public virtual async Task should_grant_and_settle_on_a_table_and_a_sequence_named_without_a_schema()
    {
        var unqualified = FencedRowTable<Guid>.Create(
            Dialect,
            schema: null,
            Fixture.UnqualifiedName(IFencedRowsFixture.WorkItemsTable),
            FencedRowKey<Guid>.Scalar("id", SqlColumnType.Guid),
            generationSequence: Fixture.UnqualifiedName(IFencedRowsFixture.GenerationSequence)
        );
        var id = await InsertAsync("pending");
        await using var connection = Fixture.CreateConnection();

        var granted = await FencedRows.GrantAsync(
            unqualified,
            connection,
            null,
            id,
            LongDuration,
            Holder("node-a"),
            AbortToken
        );

        unqualified.Name.Should().Be(Fixture.UnqualifiedName(IFencedRowsFixture.WorkItemsTable));
        granted!.Status.Should().Be(LeaseGrantStatus.Granted);
        var row = granted.Row!;
        row.Generation.Should().BePositive("the generation comes from the sequence named without a schema");
        (await FencedRows.SettleAsync(unqualified, connection, null, row, SetStatus("done"), AbortToken))
            .Should()
            .Be(LeaseSettlementStatus.Settled);

        var stored = await ReadAsync(id);
        stored.Status.Should().Be("done", "the unqualified name resolves to the same table");
        stored
            .Lease.Should()
            .Be(new RowLeaseSnapshot(row.Generation, RowLeaseState.Settled, granted.ExpiresAt, "node-a", 0));
    }

    #endregion

    #region Helpers

    protected static string Tag() => "batch-" + Guid.NewGuid().ToString("N");

    protected static LeaseGrantOptions Holder(string holder) => new() { Holder = holder };

    protected FencedRowClaim Claim(string tag, int batchSize, string? holder) =>
        new(
            "status = @Tag",
            ["id"],
            batchSize,
            LongDuration,
            holder,
            Bind: command => Dialect.AddParameter(command, "Tag", _Status, tag)
        );

    protected FencedRowWrite SetStatus(string status) =>
        new(Set: "status = @Status", Bind: command => Dialect.AddParameter(command, "Status", _Status, status));

    protected async Task<DbConnection> OpenAsync()
    {
        var connection = Fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);

        return connection;
    }

    protected async Task<Guid> InsertAsync(string status, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {WorkItems} (id, status, parent_id) VALUES (@Id, @Status, @Parent)";
        Dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
        Dialect.AddParameter(command, "Status", _Status, status);
        Dialect.AddParameter(command, "Parent", SqlColumnType.Guid, parent);
        await command.ExecuteNonQueryAsync(AbortToken);

        return id;
    }

    protected async Task<StoredWorkItem> ReadAsync(Guid id) =>
        await TryReadAsync(id) ?? throw new InvalidOperationException($"No work item has the key {id}.");

    protected async Task<StoredWorkItem?> TryReadAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT status, lease_generation, lease_state, lease_expires_at, lease_holder, lease_takeover_count
            FROM {WorkItems} WHERE id = @Id
            """;
        Dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        if (!await reader.ReadAsync(AbortToken))
        {
            return null;
        }

        return new StoredWorkItem(
            reader.GetString(0),
            new RowLeaseSnapshot(
                reader.GetInt64(1),
                (RowLeaseState)reader.GetInt16(2),
                await reader.IsDBNullAsync(3, AbortToken)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(3, AbortToken),
                await reader.IsDBNullAsync(4, AbortToken) ? null : reader.GetString(4),
                reader.GetInt32(5)
            )
        );
    }

    /// <summary>Moves the row's stored expiry into the database's past, so its attempt reads as expired at once.</summary>
    protected async Task ExpireAsync(Guid id, TimeSpan? by = null)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {WorkItems}
            SET lease_expires_at = {Dialect.ShiftByDuration("lease_expires_at", "Shift", subtract: true)}
            WHERE id = @Id
            """;
        Dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
        Dialect.AddDuration(command, "Shift", by ?? (LongDuration * 2));

        (await command.ExecuteNonQueryAsync(AbortToken)).Should().Be(1, "the work item to age must exist");
    }

    private static ValueTask<string> _ReadStatusAsync(DbDataReader reader, CancellationToken cancellationToken) =>
        ValueTask.FromResult(reader.GetString(1));

    private static FencedRowTable<Guid> _Create(TFixture fixture, string table, string? sequence) =>
        FencedRowTable<Guid>.Create(
            fixture.Dialect,
            fixture.Schema,
            table,
            FencedRowKey<Guid>.Scalar("id", SqlColumnType.Guid),
            generationSequence: sequence
        );

    #endregion
}

/// <summary>A work item as the consumer table holds it: its own status and its lease columns.</summary>
public sealed record StoredWorkItem(string Status, RowLeaseSnapshot Lease);
