// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RowLeaseRulesTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _Duration = TimeSpan.FromMinutes(5);
    private static readonly LeaseGrantOptions _Allowed = new();
    private static readonly LeaseGrantOptions _AfterSweep = new() { Takeover = LeaseTakeover.AfterSweep };

    private static RowLeaseSnapshot _Live(long generation = 3, string? holder = "node-a", int takeovers = 0) =>
        new(generation, RowLeaseState.Active, _Now.AddMinutes(1), holder, takeovers);

    private static RowLeaseSnapshot _Expired(long generation = 3, int takeovers = 0) =>
        new(generation, RowLeaseState.Active, _Now.AddMinutes(-1), "node-a", takeovers);

    private static RowLeaseSnapshot _Ended(RowLeaseState state, long generation = 3) =>
        new(generation, state, _Now.AddMinutes(-1), "node-a", 0);

    // Grant

    [Fact]
    public void should_grant_generation_one_when_the_row_was_never_granted()
    {
        // when
        var result = RowLeaseRules.Grant(default, _Now, _Duration, new LeaseGrantOptions { Holder = "node-a" });

        // then
        result.Status.Should().Be(LeaseGrantStatus.Granted);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(new RowLeaseSnapshot(1, RowLeaseState.Active, _Now + _Duration, "node-a", 0));
    }

    [Theory]
    [InlineData(RowLeaseState.Settled)]
    [InlineData(RowLeaseState.Released)]
    [InlineData(RowLeaseState.Abandoned)]
    [InlineData(RowLeaseState.Revoked)]
    public void should_grant_without_counting_a_takeover_when_the_last_attempt_ended(RowLeaseState state)
    {
        // given
        var lease = _Ended(state) with
        {
            TakeoverCount = 2,
        };

        // when
        var result = RowLeaseRules.Grant(lease, _Now, _Duration, _AfterSweep);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Granted);
        result.Wrote.Should().BeTrue();
        result.Next.Generation.Should().Be(4);
        result.Next.TakeoverCount.Should().Be(2);
    }

    [Fact]
    public void should_be_held_when_another_attempt_is_live()
    {
        // given
        var lease = _Live(holder: "node-a");

        // when
        var result = RowLeaseRules.Grant(lease, _Now, _Duration, new LeaseGrantOptions { Holder = "node-b" });

        // then
        result.Status.Should().Be(LeaseGrantStatus.Held);
        result.Wrote.Should().BeFalse();
        result.Next.Should().Be(lease);
    }

    [Fact]
    public void should_hand_back_the_live_attempt_unchanged_when_the_same_holder_grants_again()
    {
        // given
        var lease = _Live(holder: "node-a");

        // when
        var result = RowLeaseRules.Grant(lease, _Now, _Duration, new LeaseGrantOptions { Holder = "node-a" });

        // then
        result.Status.Should().Be(LeaseGrantStatus.Granted);
        result.Wrote.Should().BeFalse();
        result.Next.Should().Be(lease);
    }

    [Fact]
    public void should_be_held_when_the_holder_differs_only_by_case()
    {
        // when
        var result = RowLeaseRules.Grant(
            _Live(holder: "node-a"),
            _Now,
            _Duration,
            new LeaseGrantOptions { Holder = "NODE-A" }
        );

        // then
        result.Status.Should().Be(LeaseGrantStatus.Held);
    }

    [Fact]
    public void should_be_held_when_neither_grant_names_a_holder()
    {
        // when
        var result = RowLeaseRules.Grant(_Live(holder: null), _Now, _Duration, _Allowed);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Held);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_take_over_and_count_it_when_the_active_attempt_expired()
    {
        // when
        var result = RowLeaseRules.Grant(
            _Expired(takeovers: 1),
            _Now,
            _Duration,
            new LeaseGrantOptions { Holder = "node-b" }
        );

        // then
        result.Status.Should().Be(LeaseGrantStatus.Takeover);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(new RowLeaseSnapshot(4, RowLeaseState.Active, _Now + _Duration, "node-b", 2));
    }

    [Fact]
    public void should_treat_an_attempt_expiring_exactly_now_as_expired()
    {
        // given
        var lease = _Live() with
        {
            ExpiresAt = _Now,
        };

        // when
        var result = RowLeaseRules.Grant(lease, _Now, _Duration, _Allowed);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Takeover);
    }

    [Fact]
    public void should_refuse_as_expired_without_writing_when_the_grant_waits_for_a_sweep()
    {
        // given
        var lease = _Expired();

        // when
        var result = RowLeaseRules.Grant(lease, _Now, _Duration, _AfterSweep);

        // then
        result.Status.Should().Be(LeaseGrantStatus.Expired);
        result.Wrote.Should().BeFalse();
        result.Next.Should().Be(lease);
    }

    [Fact]
    public void should_issue_the_drawn_generation_when_one_is_given()
    {
        // when
        var result = RowLeaseRules.Grant(_Ended(RowLeaseState.Settled), _Now, _Duration, _Allowed, nextGeneration: 40);

        // then
        result.Next.Generation.Should().Be(40);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public void should_throw_when_the_drawn_generation_is_not_above_the_rows(long drawn)
    {
        // when
        var act = () => RowLeaseRules.Grant(_Ended(RowLeaseState.Settled), _Now, _Duration, _Allowed, drawn);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_when_the_grant_duration_is_not_positive()
    {
        // when
        var act = () => RowLeaseRules.Grant(default, _Now, TimeSpan.Zero, _Allowed);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_throw_when_the_grant_options_are_null()
    {
        // when
        var act = () => RowLeaseRules.Grant(default, _Now, _Duration, null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    // Renew

    [Fact]
    public void should_renew_from_now_when_the_generation_is_live()
    {
        // when
        var result = RowLeaseRules.Renew(_Live(), 3, _Now, _Duration);

        // then
        result.Status.Should().Be(LeaseRenewalStatus.Renewed);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(_Live() with { ExpiresAt = _Now + _Duration });
    }

    [Fact]
    public void should_refuse_renewal_as_expired_when_the_generation_expired()
    {
        // when
        var result = RowLeaseRules.Renew(_Expired(), 3, _Now, _Duration);

        // then
        result.Status.Should().Be(LeaseRenewalStatus.Expired);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_renewal_as_stale_when_a_newer_generation_holds_the_row()
    {
        // when
        var result = RowLeaseRules.Renew(_Live(generation: 4), 3, _Now, _Duration);

        // then
        result.Status.Should().Be(LeaseRenewalStatus.Stale);
        result.Wrote.Should().BeFalse();
    }

    [Theory]
    [InlineData(RowLeaseState.Settled, LeaseRenewalStatus.Settled)]
    [InlineData(RowLeaseState.Released, LeaseRenewalStatus.Released)]
    [InlineData(RowLeaseState.Abandoned, LeaseRenewalStatus.Abandoned)]
    [InlineData(RowLeaseState.Revoked, LeaseRenewalStatus.Revoked)]
    public void should_refuse_renewal_with_the_end_state_when_the_attempt_ended(
        RowLeaseState state,
        LeaseRenewalStatus expected
    )
    {
        // when
        var result = RowLeaseRules.Renew(_Ended(state), 3, _Now, _Duration);

        // then
        result.Status.Should().Be(expected);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_the_renewal_duration_is_not_positive()
    {
        // when
        var act = () => RowLeaseRules.Renew(_Live(), 3, _Now, TimeSpan.FromSeconds(-1));

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // Settle and release

    [Fact]
    public void should_settle_and_reset_the_takeover_count_when_the_generation_is_live()
    {
        // when
        var result = RowLeaseRules.Settle(_Live(takeovers: 2), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Settled);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(_Live() with { State = RowLeaseState.Settled, TakeoverCount = 0 });
    }

    [Fact]
    public void should_release_when_the_generation_is_live()
    {
        // when
        var result = RowLeaseRules.Release(_Live(takeovers: 1), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Released);
        result.Wrote.Should().BeTrue();
        result.Next.State.Should().Be(RowLeaseState.Released);
        result.Next.TakeoverCount.Should().Be(0);
    }

    [Fact]
    public void should_report_settled_without_writing_when_the_settlement_is_retried()
    {
        // when
        var result = RowLeaseRules.Settle(_Ended(RowLeaseState.Settled), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Settled);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_report_released_without_writing_when_the_release_is_retried()
    {
        // when
        var result = RowLeaseRules.Release(_Ended(RowLeaseState.Released), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Released);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_report_settled_when_releasing_an_attempt_that_settled()
    {
        // when
        var result = RowLeaseRules.Release(_Ended(RowLeaseState.Settled), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Settled);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_report_released_when_settling_an_attempt_that_was_released()
    {
        // when
        var result = RowLeaseRules.Settle(_Ended(RowLeaseState.Released), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Released);
        result.Wrote.Should().BeFalse();
    }

    [Theory]
    [InlineData(RowLeaseState.Abandoned, LeaseSettlementStatus.Abandoned)]
    [InlineData(RowLeaseState.Revoked, LeaseSettlementStatus.Revoked)]
    [InlineData(RowLeaseState.Idle, LeaseSettlementStatus.Stale)]
    public void should_refuse_settlement_when_the_attempt_ended_otherwise(
        RowLeaseState state,
        LeaseSettlementStatus expected
    )
    {
        // when
        var result = RowLeaseRules.Settle(_Ended(state), 3, _Now);

        // then
        result.Status.Should().Be(expected);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_settlement_as_expired_when_the_generation_expired()
    {
        // when
        var result = RowLeaseRules.Settle(_Expired(), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Expired);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_release_as_stale_when_a_newer_generation_holds_the_row()
    {
        // when
        var result = RowLeaseRules.Release(_Live(generation: 5), 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Stale);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_refuse_settlement_as_stale_when_an_older_generation_settled()
    {
        // given
        var lease = _Ended(RowLeaseState.Settled, generation: 4);

        // when
        var result = RowLeaseRules.Settle(lease, 3, _Now);

        // then
        result.Status.Should().Be(LeaseSettlementStatus.Stale);
    }

    // Revoke

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void should_revoke_an_active_attempt_whether_live_or_expired(int expiryMinutes)
    {
        // given
        var lease = _Live(takeovers: 2) with
        {
            ExpiresAt = _Now.AddMinutes(expiryMinutes),
        };

        // when
        var result = RowLeaseRules.Revoke(lease);

        // then
        result.Status.Should().Be(LeaseFenceStatus.Revoked);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(lease with { State = RowLeaseState.Revoked, TakeoverCount = 0 });
    }

    [Theory]
    [InlineData(RowLeaseState.Revoked, LeaseFenceStatus.Revoked)]
    [InlineData(RowLeaseState.Settled, LeaseFenceStatus.Settled)]
    [InlineData(RowLeaseState.Released, LeaseFenceStatus.Released)]
    [InlineData(RowLeaseState.Abandoned, LeaseFenceStatus.Abandoned)]
    [InlineData(RowLeaseState.Idle, LeaseFenceStatus.Stale)]
    public void should_report_how_the_last_attempt_ended_without_writing_when_nothing_is_active(
        RowLeaseState state,
        LeaseFenceStatus expected
    )
    {
        // when
        var result = RowLeaseRules.Revoke(_Ended(state));

        // then
        result.Status.Should().Be(expected);
        result.Wrote.Should().BeFalse();
    }

    // Judge

    [Fact]
    public void should_judge_the_live_generation_current()
    {
        RowLeaseRules.Judge(_Live(), 3, _Now).Should().Be(LeaseFenceStatus.Current);
    }

    [Fact]
    public void should_judge_the_expired_generation_expired()
    {
        RowLeaseRules.Judge(_Expired(), 3, _Now).Should().Be(LeaseFenceStatus.Expired);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void should_judge_any_other_generation_stale(long generation)
    {
        RowLeaseRules.Judge(_Live(), generation, _Now).Should().Be(LeaseFenceStatus.Stale);
    }

    [Theory]
    [InlineData(RowLeaseState.Settled, LeaseFenceStatus.Settled)]
    [InlineData(RowLeaseState.Released, LeaseFenceStatus.Released)]
    [InlineData(RowLeaseState.Abandoned, LeaseFenceStatus.Abandoned)]
    [InlineData(RowLeaseState.Revoked, LeaseFenceStatus.Revoked)]
    public void should_judge_an_ended_generation_by_its_end_state(RowLeaseState state, LeaseFenceStatus expected)
    {
        RowLeaseRules.Judge(_Ended(state), 3, _Now).Should().Be(expected);
    }

    [Fact]
    public void should_judge_generation_zero_of_an_idle_row_stale()
    {
        RowLeaseRules.Judge(default, 0, _Now).Should().Be(LeaseFenceStatus.Stale);
    }

    [Fact]
    public void should_throw_when_the_lease_carries_an_unknown_state()
    {
        // given
        var lease = _Ended((RowLeaseState)42);

        // when
        var act = () => RowLeaseRules.Judge(lease, 3, _Now);

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    // Abandon

    [Fact]
    public void should_abandon_and_count_a_takeover_when_the_active_attempt_expired()
    {
        // when
        var result = RowLeaseRules.AbandonIfExpired(_Expired(takeovers: 1), _Now);

        // then
        result.Status.Should().Be(LeaseFenceStatus.Abandoned);
        result.Wrote.Should().BeTrue();
        result.Next.Should().Be(_Expired() with { State = RowLeaseState.Abandoned, TakeoverCount = 2 });
    }

    [Fact]
    public void should_leave_a_live_attempt_alone_when_sweeping()
    {
        // when
        var result = RowLeaseRules.AbandonIfExpired(_Live(), _Now);

        // then
        result.Status.Should().Be(LeaseFenceStatus.Current);
        result.Wrote.Should().BeFalse();
    }

    [Fact]
    public void should_leave_an_ended_attempt_alone_when_sweeping()
    {
        // when
        var result = RowLeaseRules.AbandonIfExpired(_Ended(RowLeaseState.Settled), _Now);

        // then
        result.Status.Should().Be(LeaseFenceStatus.Settled);
        result.Wrote.Should().BeFalse();
    }

    // Sequences

    [Fact]
    public void should_refuse_the_previous_generation_after_a_grant_ends_and_the_row_is_granted_again()
    {
        // given
        var first = RowLeaseRules.Grant(default, _Now, _Duration, _Allowed).Next;
        var settled = RowLeaseRules.Settle(first, first.Generation, _Now).Next;
        var second = RowLeaseRules.Grant(settled, _Now, _Duration, _Allowed).Next;

        // when
        var stale = RowLeaseRules.Settle(second, first.Generation, _Now);

        // then
        second.Generation.Should().Be(2);
        stale.Status.Should().Be(LeaseSettlementStatus.Stale);
        stale.Wrote.Should().BeFalse();
    }
}
