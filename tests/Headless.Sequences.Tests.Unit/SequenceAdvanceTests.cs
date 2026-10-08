// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class SequenceAdvanceTests : TestBase
{
    [Theory]
    [InlineData(0L, 1L, SequenceAdvanceStatus.Next, null)]
    [InlineData(0L, 4L, SequenceAdvanceStatus.Skipped, null)]
    [InlineData(0L, 0L, SequenceAdvanceStatus.Stale, null)]
    [InlineData(7L, 8L, SequenceAdvanceStatus.Next, 7L)]
    [InlineData(7L, 10L, SequenceAdvanceStatus.Skipped, 7L)]
    [InlineData(7L, 7L, SequenceAdvanceStatus.Stale, 7L)]
    [InlineData(7L, 3L, SequenceAdvanceStatus.Stale, 7L)]
    public async Task should_classify_a_reported_value_against_the_value_stored_before(
        long stored,
        long reported,
        SequenceAdvanceStatus expected,
        long? previous
    )
    {
        // given
        var context = _Reported(out var unit);
        context
            .Store.AdvanceEnlistedAsync(unit, new SequenceKey("", "pos", "terminal-1"), reported, 0, AbortToken)
            .Returns(stored);

        // when
        var advance = await context.Feature.AdvanceToAsync(unit, "pos", reported, "terminal-1", AbortToken);

        // then
        advance.Should().Be(new SequenceAdvance(expected, previous));
        advance.IsAccepted.Should().Be(expected != SequenceAdvanceStatus.Stale);
    }

    [Fact]
    public async Task should_create_the_counter_one_step_below_the_policy_start()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["pos"] = new SequencePolicy
        {
            Mode = SequenceMode.Reported,
            Start = 100,
            Step = 5,
        };
        var (unit, _) = SequenceTestContext.ActiveUnit();
        context.Store.AdvanceEnlistedAsync(unit, Arg.Any<SequenceKey>(), 100, 95, AbortToken).Returns(95);

        // when
        var advance = await context.Feature.AdvanceToAsync(unit, "pos", 100, cancellationToken: AbortToken);

        // then
        advance.Should().Be(new SequenceAdvance(SequenceAdvanceStatus.Next, Previous: null));
    }

    [Fact]
    public async Task should_refuse_a_counter_not_registered_as_reported_before_the_store()
    {
        // given
        var context = new SequenceTestContext().GapFree("receipt");
        var (unit, _) = SequenceTestContext.ActiveUnit();

        // when
        var act = async () => await context.Feature.AdvanceToAsync(unit, "receipt", 1, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SequenceMode.Reported*");
        context.Store.ReceivedCalls().Should().BeEmpty();
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_a_reported_counter_on_every_issuing_entry_point()
    {
        // given
        var context = _Reported(out var unit);

        // when
        var next = async () => await context.Feature.NextAsync(unit, "pos", cancellationToken: AbortToken);
        var number = async () => await context.Feature.NextNumberAsync(unit, "pos", AbortToken);
        var fast = async () => await context.Generator.NextAsync("pos", cancellationToken: AbortToken);

        // then
        await next.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AdvanceToAsync*");
        await number.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AdvanceToAsync*");
        await fast.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AdvanceToAsync*");
        context.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_mark_an_observed_unit_non_retryable_after_the_checks()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["pos"] = new SequencePolicy { Mode = SequenceMode.Reported };
        var (unit, _) = SequenceTestContext.ActiveUnit(isOwned: false);

        // when
        await context.Feature.AdvanceToAsync(unit, "pos", 1, cancellationToken: AbortToken);

        // then
        unit.Received(1).PreventRetry();
    }

    private static SequenceTestContext _Reported(out IUnitOfWork unit)
    {
        var context = new SequenceTestContext();
        context.Options.Policies["pos"] = new SequencePolicy { Mode = SequenceMode.Reported };
        (unit, _) = SequenceTestContext.ActiveUnit();

        return context;
    }
}
