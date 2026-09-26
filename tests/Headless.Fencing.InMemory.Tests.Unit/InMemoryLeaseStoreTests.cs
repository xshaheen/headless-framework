// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryLeaseStoreTests : TestBase
{
    private static readonly TimeSpan _Duration = TimeSpan.FromMinutes(1);

    /// <summary>Long enough that a call not blocked on a held key has certainly returned.</summary>
    private static readonly TimeSpan _Returns = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_decide_expiry_by_the_injected_clock()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        await using var services = _CreateServices(clock);
        var leases = services.GetRequiredService<IFencedLeases>();

        // when
        var granted = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);
        clock.Advance(TimeSpan.FromSeconds(59));
        var held = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);
        var renewed = await leases.RenewAsync(granted.Lease!, _Duration, AbortToken);
        clock.Advance(_Duration);
        var takeover = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);

        // then — an expiry at the clock's instant is already expired
        granted.Status.Should().Be(LeaseGrantStatus.Granted);
        granted.ExpiresAt.Should().Be(new DateTimeOffset(2026, 9, 26, 12, 1, 0, TimeSpan.Zero));
        held.Status.Should().Be(LeaseGrantStatus.Held);
        held.HolderGeneration.Should().Be(granted.Lease!.Generation);
        renewed
            .Should()
            .Be(
                new LeaseRenewalResult(
                    LeaseRenewalStatus.Renewed,
                    new DateTimeOffset(2026, 9, 26, 12, 1, 59, TimeSpan.Zero)
                )
            );
        takeover.Status.Should().Be(LeaseGrantStatus.Takeover);
        takeover.PreviousGeneration.Should().Be(granted.Lease.Generation);
        (await leases.SettleAsync(granted.Lease, AbortToken)).Should().Be(LeaseSettlementStatus.Stale);
    }

    [Fact]
    public async Task should_sweep_a_lease_once_the_injected_clock_passes_its_expiry()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        await using var services = _CreateServices(clock);
        var leases = services.GetRequiredService<IFencedLeases>();
        var granted = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);

        // when
        var early = await leases.SweepExpiredAsync("job", (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);
        clock.Advance(_Duration);
        var late = await leases.SweepExpiredAsync("job", (_, _, _) => ValueTask.CompletedTask, 10, AbortToken);

        // then
        early.Handled.Should().BeEmpty("the lease is still live on the injected clock");
        late.Handled.Should().ContainSingle().Which.Generation.Should().Be(granted.Lease!.Generation);
        (await leases.RenewAsync(granted.Lease, _Duration, AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Abandoned);
    }

    [Fact]
    public async Task should_grant_exactly_one_holder_when_many_grants_race_in_one_process()
    {
        // given
        await using var services = _CreateServices();
        var leases = services.GetRequiredService<IFencedLeases>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var racers = Enumerable
            .Range(0, 64)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await start.Task;

                        return await leases.GrantAsync("job", "order-1", _Duration, AbortToken);
                    },
                    AbortToken
                )
            )
            .ToList();

        // when
        start.SetResult();
        var results = await Task.WhenAll(racers);

        // then
        var winner = results.Should().ContainSingle(static r => r.IsAcquired).Subject;
        winner.Status.Should().Be(LeaseGrantStatus.Granted);
        results
            .Where(static r => !r.IsAcquired)
            .Should()
            .HaveCount(63)
            .And.AllSatisfy(r => r.HolderGeneration.Should().Be(winner.Lease!.Generation));
    }

    [Fact]
    public async Task should_refuse_a_unit_over_a_database_connection_before_any_write()
    {
        // given
        await using var services = _CreateServices();
        var enlisted = services.GetRequiredService<IUnitOfWorkLeases>();
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(Substitute.For<IRelationalUnitOfWorkResource>());

        // when
        var act = async () => await enlisted.GrantAsync(unit, "job", "order-1", _Duration, AbortToken);

        // then — lease state in this process cannot commit or roll back with a database transaction
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database transaction*InMemory*");
        unit.DidNotReceive().PreventRetry();
        (await services.GetRequiredService<IFencedLeases>().GrantAsync("job", "order-1", _Duration, AbortToken))
            .Status.Should()
            .Be(LeaseGrantStatus.Granted, "the refused call wrote nothing");
    }

    [Fact]
    public async Task should_let_one_unit_fence_and_then_settle_the_same_lease()
    {
        // given
        await using var services = _CreateServices();
        var leases = services.GetRequiredService<IFencedLeases>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var granted = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);

        // when — the unit already holds the key after the fence, so the settlement must not wait on itself
        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            await unit.Leases.FenceAsync(granted.Lease!, AbortToken);
            var settled = await unit
                .Leases.SettleAsync(granted.Lease!, AbortToken)
                .AsTask()
                .WaitAsync(_Returns, AbortToken);
            var fenceAfterSettle = async () => await unit.Leases.FenceAsync(granted.Lease!, AbortToken);

            (await fenceAfterSettle.Should().ThrowAsync<StaleLeaseException>())
                .Which.Reason.Should()
                .Be(LeaseFenceStatus.Settled, "the unit reads its own settlement");

            settled.Should().Be(LeaseSettlementStatus.Settled);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        (await leases.RenewAsync(granted.Lease!, _Duration, AbortToken))
            .Status.Should()
            .Be(LeaseRenewalStatus.Settled);
        var next = await leases
            .GrantAsync("job", "order-1", _Duration, AbortToken)
            .AsTask()
            .WaitAsync(_Returns, AbortToken);
        next.Status.Should().Be(LeaseGrantStatus.Granted, "the completed unit released the key");
    }

    [Fact]
    public async Task should_release_the_key_and_drop_the_writes_when_a_unit_rolls_back()
    {
        // given
        await using var services = _CreateServices();
        var leases = services.GetRequiredService<IFencedLeases>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var granted = await leases.GrantAsync("job", "order-1", _Duration, AbortToken);

        // when
        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            (await unit.Leases.SettleAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
            await unit.RollbackAsync();
        }

        // then — the settlement never happened, and the key is free for the next call at once
        var renewed = await leases
            .RenewAsync(granted.Lease!, _Duration, AbortToken)
            .AsTask()
            .WaitAsync(_Returns, AbortToken);
        renewed.Status.Should().Be(LeaseRenewalStatus.Renewed);
    }

    [Fact]
    public async Task should_release_the_key_and_drop_the_writes_when_a_unit_is_disposed_without_completing()
    {
        // given
        await using var services = _CreateServices();
        var leases = services.GetRequiredService<IFencedLeases>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();

        // when
        long abandoned;

        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            abandoned = (await unit.Leases.GrantAsync("job", "order-1", _Duration, AbortToken)).Lease!.Generation;
        }

        // then
        var next = await leases
            .GrantAsync("job", "order-1", _Duration, AbortToken)
            .AsTask()
            .WaitAsync(_Returns, AbortToken);
        next.Status.Should().Be(LeaseGrantStatus.Granted, "the abandoned grant left no lease behind");
        next.Lease!.Generation.Should().BeGreaterThan(abandoned);
    }

    [Fact]
    public void should_name_the_in_memory_provider_and_refuse_a_second_one()
    {
        // when
        var none = () => new ServiceCollection().AddHeadlessFencing(static _ => { });
        var two = () =>
            new ServiceCollection().AddHeadlessFencing(static setup =>
            {
                setup.UseInMemory();
                setup.UseInMemory();
            });

        // then
        none.Should().Throw<InvalidOperationException>().WithMessage("*`UseInMemory`*");
        two.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    private static ServiceProvider _CreateServices(TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddHeadlessFencing(setup => setup.UseInMemory());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
