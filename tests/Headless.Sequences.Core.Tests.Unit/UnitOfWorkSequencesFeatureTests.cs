// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class UnitOfWorkSequencesFeatureTests : TestBase
{
    [Fact]
    public async Task should_increment_on_the_unit_resource_with_the_policy_start_and_step()
    {
        // given
        var context = new SequenceTestContext();
        context.Options.Policies["invoice"] = new SequencePolicy
        {
            Mode = SequenceMode.GapFree,
            Start = 1000,
            Step = 10,
        };
        context.Tenant.Id = "t1";
        var (unit, resource) = SequenceTestContext.ActiveUnit();

        // when
        var value = await context.Feature.NextAsync(unit, "invoice", "2026", AbortToken);

        // then
        value.Should().Be(1000);
        await context
            .Store.Received(1)
            .IncrementEnlistedAsync(resource, new SequenceKey("t1", "invoice", "2026"), 1000, 10, AbortToken);
        await context.Store.DidNotReceiveWithAnyArgs().IncrementAsync(default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_fast_name_naming_the_injected_generator_before_the_store()
    {
        // given
        var context = new SequenceTestContext();
        var (unit, _) = SequenceTestContext.ActiveUnit();

        // when
        var act = async () => await context.Feature.NextAsync(unit, "receipt", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISequenceGenerator*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_validate_arguments_before_the_mode_check()
    {
        // given — a fast default policy would refuse the mode, but the blank name must be reported first
        var context = new SequenceTestContext();
        var (unit, _) = SequenceTestContext.ActiveUnit();

        // when
        var act = async () => await context.Feature.NextAsync(unit, "  ", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _AssertStoreUntouched(context);
    }

    [Theory]
    [InlineData(UnitOfWorkState.Completed)]
    [InlineData(UnitOfWorkState.Failed)]
    public async Task should_refuse_a_unit_that_is_not_active_before_the_store(UnitOfWorkState state)
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, _) = SequenceTestContext.ActiveUnit(isOwned: false);
        unit.State.Returns(state);

        // when
        var act = async () => await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{state}*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_a_unit_without_a_relational_resource_before_the_store()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(Substitute.For<IUnitOfWorkResource>());

        // when
        var act = async () => await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*relational resource*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_refuse_a_unit_whose_transaction_completed_before_the_store()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, resource) = SequenceTestContext.ActiveUnit(isOwned: false);
        resource.IsTransactionCompleted.Returns(true);

        // when
        var act = async () => await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already completed*");
        _AssertStoreUntouched(context);
        unit.DidNotReceive().PreventRetry();
    }

    [Fact]
    public async Task should_name_the_gap_free_operation_in_the_refusal()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, _) = SequenceTestContext.ActiveUnit();
        unit.State.Returns(UnitOfWorkState.Completed);

        // when
        var act = async () => await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*gap-free sequence*");
    }

    [Fact]
    public async Task should_not_prevent_retry_when_the_store_refuses_the_resource()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, resource) = SequenceTestContext.ActiveUnit(isOwned: false);
        context
            .Store.When(store => store.ValidateEnlistment(resource))
            .Do(_ => throw new InvalidOperationException("different database"));

        // when
        var act = async () => await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("different database");
        unit.DidNotReceive().PreventRetry();
        await context
            .Store.DidNotReceiveWithAnyArgs()
            .IncrementEnlistedAsync(default!, default, default, default, AbortToken);
    }

    [Fact]
    public async Task should_prevent_retry_on_an_observed_resource_between_validation_and_increment()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, resource) = SequenceTestContext.ActiveUnit(isOwned: false);

        // when
        await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        Received.InOrder(() =>
        {
            context.Store.ValidateEnlistment(resource);
            unit.PreventRetry();
            _ = context.Store.IncrementEnlistedAsync(
                resource,
                Arg.Any<SequenceKey>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            );
        });
    }

    [Fact]
    public async Task should_keep_an_owned_resource_replayable()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, resource) = SequenceTestContext.ActiveUnit(isOwned: true);

        // when
        await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        unit.DidNotReceive().PreventRetry();
        context.Store.Received(1).ValidateEnlistment(resource);
    }

    [Fact]
    public async Task should_hand_the_store_the_unit_resource_whatever_its_transaction_type()
    {
        // given — the provider, not Core, decides which transaction types it can write through
        var context = new SequenceTestContext().GapFree("invoice");
        var (unit, resource) = SequenceTestContext.ActiveUnit();
        resource.Transaction.Returns(Substitute.For<DbTransaction>());

        // when
        await context.Feature.NextAsync(unit, "invoice", cancellationToken: AbortToken);

        // then
        context.Store.Received(1).ValidateEnlistment(resource);
    }

    [Fact]
    public async Task should_refuse_a_null_unit()
    {
        // given
        var context = new SequenceTestContext().GapFree("invoice");

        // when
        var act = async () => await context.Feature.NextAsync(null!, "invoice", cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
        _AssertStoreUntouched(context);
    }

    private static void _AssertStoreUntouched(SequenceTestContext context)
    {
        context.Store.ReceivedCalls().Should().BeEmpty();
    }
}
