// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Ledger;

namespace Tests;

public sealed class LedgerVerdictTests : TestBase
{
    [Fact]
    public void should_report_lost_duplicated_and_unexpected_messages()
    {
        // given
        var ledger = new MessageLedger(TimeProvider.System);
        ledger.RecordPublished("once", "host-0");
        ledger.RecordStarted("once", "host-0");
        ledger.RecordCompleted("once", "host-0");
        ledger.RecordPublished("lost", "host-0");
        ledger.RecordPublished("twice", "host-0");
        ledger.RecordStarted("twice", "host-0");
        ledger.RecordCompleted("twice", "host-0");
        ledger.RecordStarted("twice", "host-1");
        ledger.RecordCompleted("twice", "host-1");
        ledger.RecordPublishRejected("refused");
        ledger.RecordStarted("refused", "host-0");
        ledger.RecordCompleted("refused", "host-0");

        // when
        var verdict = LedgerVerdict.Evaluate(ledger.Snapshot());

        // then
        verdict.Accepted.Should().Be(3);
        verdict.Rejected.Should().Be(1);
        verdict.Completions.Should().Be(4);
        verdict.Lost.Should().Equal("lost");
        verdict.Duplicated.Should().Equal("twice");
        verdict.Unexpected.Should().Equal("refused");
        verdict.LossViolations().Should().ContainSingle().Which.Should().Contain("lost");
        verdict.ExactlyOnceViolations().Should().HaveCount(2);
    }

    [Fact]
    public void should_fail_the_acceptance_floor_when_nothing_was_accepted()
    {
        var verdict = _Verdict(accepted: 0, rejected: 40);

        verdict
            .AcceptanceViolations(attempted: 40, minimumShare: 0)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("none of 40");
    }

    [Fact]
    public void should_fail_the_acceptance_floor_when_nothing_was_attempted()
    {
        var verdict = _Verdict(accepted: 0, rejected: 0);

        verdict
            .AcceptanceViolations(attempted: 0, minimumShare: 0)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no publish");
    }

    [Fact]
    public void should_fail_the_acceptance_floor_when_too_few_publishes_were_accepted()
    {
        var verdict = _Verdict(accepted: 19, rejected: 21);

        verdict
            .AcceptanceViolations(attempted: 40, minimumShare: 0.5)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("only 19 of 40");
    }

    [Fact]
    public void should_pass_the_acceptance_floor_at_the_minimum_share()
    {
        var verdict = _Verdict(accepted: 20, rejected: 20);

        verdict.AcceptanceViolations(attempted: 40, minimumShare: 0.5).Should().BeEmpty();
    }

    [Fact]
    public void should_judge_duplicates_against_the_no_loss_limit()
    {
        var verdict = _Verdict(accepted: 100, rejected: 0, duplicated: 2);

        verdict.NoLossViolations(maxDuplicateRatio: 0.05).Should().BeEmpty();
        verdict.NoLossViolations(maxDuplicateRatio: 0.01).Should().ContainSingle().Which.Should().Contain("2 of 100");
    }

    private static LedgerVerdict _Verdict(int accepted, int rejected, int duplicated = 0) =>
        new(
            accepted,
            rejected,
            accepted + duplicated,
            Lost: [],
            Duplicated: [.. Enumerable.Range(0, duplicated).Select(index => $"dup-{index}")],
            Unexpected: [],
            Exhausted: []
        );
}
