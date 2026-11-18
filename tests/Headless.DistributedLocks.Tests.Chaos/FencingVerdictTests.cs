// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Oracle;

namespace Tests;

public sealed class FencingVerdictTests : TestBase
{
    [Fact]
    public void should_pass_when_grants_increase_and_a_stale_write_is_rejected()
    {
        // given — w1's session dies mid-hold, w2 takes over with a higher token and writes first
        var log = new LogBuilder()
            .Grant("w1", "r", token: 1, highWaterMark: 0)
            .Grant("w2", "r", token: 2, highWaterMark: 0)
            .Write("w2", "r", token: 2, accepted: true)
            .Write("w1", "r", token: 1, accepted: false)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);

        // then
        verdict.Violations.Should().BeEmpty();
        verdict.RejectedWrites.Should().Be(1);
        verdict.Evidence.Should().BeNull();
    }

    [Fact]
    public void should_report_the_issuer_rule_when_a_grant_reuses_an_accepted_token()
    {
        // given — the counter rolled back: token 2 was accepted, then a new grant carries 2 again
        var log = new LogBuilder()
            .Grant("w1", "r", token: 2, highWaterMark: 0)
            .Write("w1", "r", token: 2, accepted: true)
            .Release("w1", "r", token: 2)
            .Grant("w2", "r", token: 2, highWaterMark: 2)
            .Write("w2", "r", token: 2, accepted: false)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);

        // then
        verdict.Violations.Should().ContainSingle().Which.Should().StartWith(FencingVerdict.IssuerRule + ":");
        verdict.DuplicateGrantTokens.Should().Be(1);
        verdict.Evidence.Should().Contain("w2 r Granted token 2 (high-water 2)");
    }

    [Fact]
    public void should_fail_a_reissued_token_under_a_durable_sequence_but_only_report_it_under_redis_failover()
    {
        // given — the history a Redis failover produced: token 713 accepted, then granted again on the promoted replica
        var log = new LogBuilder()
            .Grant("w6", "r", token: 713, highWaterMark: 712)
            .Write("w6", "r", token: 713, accepted: true)
            .Release("w6", "r", token: 713)
            .Grant("w7", "r", token: 713, highWaterMark: 713)
            .Write("w7", "r", token: 713, accepted: false)
            .Events;

        // when
        var durable = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);
        var redis = FencingVerdict.Evaluate(log, FencingContract.RedisFailover);

        // then
        durable.Violations.Should().ContainSingle().Which.Should().StartWith(FencingVerdict.IssuerRule + ":");
        durable.Observations.Should().BeEmpty();
        redis.Violations.Should().BeEmpty();
        redis.Observations.Should().ContainSingle().Which.Should().StartWith(FencingVerdict.IssuerRule + ":");
        redis.ObservationEvidence.Should().Contain("w7 r Granted token 713 (high-water 713)");
        (durable.IssuerBreaks, redis.IssuerBreaks).Should().Be((1, 1));
    }

    [Fact]
    public void should_fail_the_accepted_order_rule_under_redis_failover_too()
    {
        // given
        var log = new LogBuilder()
            .Grant("w1", "r", token: 5, highWaterMark: 0)
            .Write("w1", "r", token: 5, accepted: true)
            .Write("w2", "r", token: 5, accepted: true)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.RedisFailover);

        // then
        verdict.Violations.Should().ContainSingle().Which.Should().StartWith(FencingVerdict.AcceptedOrderRule + ":");
    }

    [Fact]
    public void should_report_the_accepted_order_rule_when_the_store_accepts_a_lower_token()
    {
        // given — a store that compared for equality instead of order would accept 3 after 5
        var log = new LogBuilder()
            .Grant("w1", "r", token: 5, highWaterMark: 0)
            .Write("w1", "r", token: 5, accepted: true)
            .Grant("w2", "r", token: 6, highWaterMark: 5)
            .Write("w2", "r", token: 3, accepted: true)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);

        // then
        verdict.Violations.Should().ContainSingle().Which.Should().StartWith(FencingVerdict.AcceptedOrderRule + ":");
    }

    [Fact]
    public void should_count_a_rolled_back_grant_without_failing_when_no_write_was_accepted_above_it()
    {
        // given — a failover reissued token 3 to w2, but w1 never wrote with it, so nothing stale lands
        var log = new LogBuilder()
            .Grant("w1", "r", token: 3, highWaterMark: 0)
            .Release("w1", "r", token: 3)
            .Grant("w2", "r", token: 3, highWaterMark: 0)
            .Write("w2", "r", token: 3, accepted: true)
            .Grant("w3", "r", token: 2, highWaterMark: 3)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);

        // then — only the last grant, below the accepted 3, breaks the issuer rule
        verdict
            .Violations.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("token 2 after the resource had accepted token 3");
        verdict.DuplicateGrantTokens.Should().Be(1);
        verdict.RolledBackGrants.Should().Be(1);
    }

    [Fact]
    public void should_judge_each_resource_on_its_own_tokens()
    {
        // given — database sequences are shared across resources, so a low token on another resource is fine
        var log = new LogBuilder()
            .Grant("w1", "a", token: 10, highWaterMark: 0)
            .Write("w1", "a", token: 10, accepted: true)
            .Grant("w2", "b", token: 4, highWaterMark: 0)
            .Write("w2", "b", token: 4, accepted: true)
            .Events;

        // when
        var verdict = FencingVerdict.Evaluate(log, FencingContract.DurableSequence);

        // then
        verdict.Violations.Should().BeEmpty();
    }

    [Fact]
    public void should_count_overlapping_holders_when_a_second_grant_starts_before_the_first_releases()
    {
        // given
        var ledger = new FencingLedger(TimeProvider.System);

        // when
        ledger.Granted("w1", "r", 1, 0);
        ledger.Granted("w2", "r", 2, 0);
        ledger.Released("w1", "r", 1);
        ledger.Granted("w3", "r", 3, 0);

        // then
        ledger.MaxConcurrentHolders.Should().Be(2);
        ledger.OverlappingHolds.Should().Be(2);
    }

    [Fact]
    public void should_reject_a_write_at_or_below_the_high_water_mark()
    {
        // given
        var ledger = new FencingLedger(TimeProvider.System);
        var store = new FencedResourceStore(ledger);

        // when
        var first = store.TryWrite("w1", "r", new(5));
        var equal = store.TryWrite("w2", "r", new(5));
        var lower = store.TryWrite("w3", "r", new(4));
        var higher = store.TryWrite("w4", "r", new(6));

        // then
        (first, equal, lower, higher)
            .Should()
            .Be((true, false, false, true));
        store.HighWaterMark("r").Should().Be(6);
    }

    private sealed class LogBuilder
    {
        private readonly List<FencingEvent> _events = [];

        public IReadOnlyList<FencingEvent> Events => _events;

        public LogBuilder Grant(string worker, string resource, long token, long highWaterMark) =>
            _Add(worker, resource, FencingEventKind.Granted, token, highWaterMark);

        public LogBuilder Write(string worker, string resource, long token, bool accepted) =>
            _Add(
                worker,
                resource,
                accepted ? FencingEventKind.WriteAccepted : FencingEventKind.WriteRejected,
                token,
                0
            );

        public LogBuilder Release(string worker, string resource, long token) =>
            _Add(worker, resource, FencingEventKind.Released, token, 0);

        private LogBuilder _Add(string worker, string resource, FencingEventKind kind, long token, long highWaterMark)
        {
            _events.Add(new FencingEvent(_events.Count, TimeSpan.Zero, worker, resource, kind, token, highWaterMark));

            return this;
        }
    }
}
