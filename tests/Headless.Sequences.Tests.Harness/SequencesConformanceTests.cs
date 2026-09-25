// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// The provider-neutral contract of tenant-scoped sequences, run against a real database. A provider leaf derives
/// from it, supplies its <see cref="ISequencesFixture" />, and overrides every test with <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// Each test numbers its own freshly named counters, so tests never share a row and need no cleanup between them.
/// </remarks>
#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
public abstract class SequencesConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ISequencesFixture
{
    protected TFixture Fixture { get; } = fixture;

    #region Fast mode

    public virtual async Task should_issue_exactly_one_to_hundred_when_two_hosts_call_concurrently()
    {
        var name = CreateName("receipt");
        const string tenant = "tenant-ae1";
        await using var hostA = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        long[] values;

        using (hostA.CurrentTenant.Change(tenant))
        {
            values = await Task.WhenAll(
                Enumerable
                    .Range(0, 100)
                    .Select(i =>
                        Task.Run(
                            async () =>
                                await (i % 2 == 0 ? hostA : hostB).Generator.NextAsync(
                                    name,
                                    cancellationToken: AbortToken
                                ),
                            AbortToken
                        )
                    )
            );
        }

        values.Should().BeEquivalentTo(Enumerable.Range(1, 100).Select(static x => (long)x));
        (await Fixture.ReadValueAsync(new SequenceKey(tenant, name, ""), AbortToken)).Should().Be(100);
    }

    public virtual async Task should_create_the_counter_once_when_first_calls_race_on_a_new_key()
    {
        var name = CreateName("first-use");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var values = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ =>
                    Task.Run(
                        async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken),
                        AbortToken
                    )
                )
        );

        values.Should().BeEquivalentTo(Enumerable.Range(1, 20).Select(static x => (long)x));
    }

    public virtual async Task should_keep_counters_of_different_tenants_and_the_host_scope_independent()
    {
        var name = CreateName("receipt");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        using (host.CurrentTenant.Change("tenant-a"))
        {
            for (var i = 1; i <= 3; i++)
            {
                (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(i);
            }
        }

        using (host.CurrentTenant.Change("tenant-b"))
        {
            (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
        }

        (await host.Generator.NextAsync(name, cancellationToken: AbortToken))
            .Should()
            .Be(1, "the host scope is its own counter");

        (await Fixture.ReadValueAsync(new SequenceKey("tenant-a", name, ""), AbortToken)).Should().Be(3);
        (await Fixture.ReadValueAsync(new SequenceKey("tenant-b", name, ""), AbortToken)).Should().Be(1);
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);
    }

    public virtual async Task should_keep_partitions_of_one_name_independent()
    {
        var name = CreateName("invoice");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Generator.NextAsync(name, "2025", AbortToken)).Should().Be(1);
        (await host.Generator.NextAsync(name, "2025", AbortToken)).Should().Be(2);
        (await host.Generator.NextAsync(name, "2026", AbortToken)).Should().Be(1);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken))
            .Should()
            .Be(1, "no partition is its own counter");
    }

    public virtual async Task should_treat_a_null_and_an_empty_partition_as_the_same_counter()
    {
        var name = CreateName("no-partition");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Generator.NextAsync(name, partition: null, AbortToken)).Should().Be(1);
        (await host.Generator.NextAsync(name, partition: "", AbortToken)).Should().Be(2);
    }

    public virtual async Task should_treat_names_differing_only_by_case_as_different_counters()
    {
        var prefix = Guid.NewGuid().ToString("N");
        var upper = $"{prefix}-INV";
        var lower = $"{prefix}-inv";
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        (await host.Generator.NextAsync(upper, cancellationToken: AbortToken)).Should().Be(1);
        (await host.Generator.NextAsync(upper, cancellationToken: AbortToken)).Should().Be(2);
        (await host.Generator.NextAsync(lower, cancellationToken: AbortToken)).Should().Be(1);
    }

    public virtual async Task should_apply_the_registered_start_and_step()
    {
        var name = CreateName("stepped");
        await using var host = await Fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Start = 1000, Step = 10 }),
            AbortToken
        );

        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1000);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1010);

        var range = await host.Generator.ReserveAsync(name, 3, cancellationToken: AbortToken);

        range.Should().Equal(1020L, 1030L, 1040L);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1050);
    }

    public virtual async Task should_start_a_reservation_on_a_new_key_at_the_policy_start()
    {
        var name = CreateName("reserve-new");
        await using var host = await Fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Start = 500, Step = 5 }),
            AbortToken
        );

        var range = await host.Generator.ReserveAsync(name, 4, cancellationToken: AbortToken);

        range.Should().Equal(500L, 505L, 510L, 515L);
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(515);
    }

    public virtual async Task should_reserve_a_consecutive_range_and_continue_after_it()
    {
        var name = CreateName("batch");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);

        var range = await host.Generator.ReserveAsync(name, 50, cancellationToken: AbortToken);

        range.First.Should().Be(2);
        range.Count.Should().Be(50);
        range.Step.Should().Be(1);
        range.Last.Should().Be(51);
        range.Should().Equal(Enumerable.Range(2, 50).Select(static x => (long)x));
        (await host.Generator.NextAsync(name, cancellationToken: AbortToken)).Should().Be(range.Last + range.Step);
    }

    public virtual async Task should_never_overlap_concurrent_reservations()
    {
        var name = CreateName("batch-race");
        await using var hostA = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var ranges = await Task.WhenAll(
            Enumerable
                .Range(0, 10)
                .Select(i =>
                    Task.Run(
                        async () =>
                            await (i % 2 == 0 ? hostA : hostB).Generator.ReserveAsync(
                                name,
                                10,
                                cancellationToken: AbortToken
                            ),
                        AbortToken
                    )
                )
        );

        ranges
            .SelectMany(static range => range)
            .Should()
            .BeEquivalentTo(Enumerable.Range(1, 100).Select(static x => (long)x));
    }

    public virtual async Task should_throw_and_leave_the_counter_unchanged_when_cancelled_before_the_call()
    {
        var name = CreateName("cancelled");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await host.Generator.NextAsync(name, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_advance_at_most_one_step_per_call_cancelled_in_flight()
    {
        // A call cancelled after its commit reached the server can still report cancellation, so a cancelled call
        // may or may not have consumed its number. What must never happen is a call advancing the counter twice or
        // a completed call's number being handed out again.
        var name = CreateName("cancel-race");
        const int attempts = 20;
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        var issued = new List<long>();

        for (var i = 0; i < attempts; i++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(i));

            try
            {
                issued.Add(await host.Generator.NextAsync(name, cancellationToken: cts.Token));
            }
            catch (OperationCanceledException)
            {
                // Expected for the calls the token beat; the counter check below covers them.
            }
        }

        var next = await host.Generator.NextAsync(name, cancellationToken: AbortToken);

        issued.Should().OnlyHaveUniqueItems();
        issued.Should().BeInAscendingOrder();
        issued.Should().AllSatisfy(value => value.Should().BeLessThan(next));
        next.Should().BeInRange(issued.Count + 1, attempts + 1);
    }

    public virtual async Task should_reject_invalid_key_parts_before_any_statement()
    {
        var name = CreateName("invalid");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        var tooLongName = async () =>
            await host.Generator.NextAsync(
                new string('n', SequenceFieldLimits.NameMaxLength + 1),
                cancellationToken: AbortToken
            );
        var blankPartition = async () => await host.Generator.NextAsync(name, "   ", AbortToken);
        var tooLongPartition = async () =>
            await host.Generator.NextAsync(
                name,
                new string('p', SequenceFieldLimits.PartitionMaxLength + 1),
                AbortToken
            );

        // A padded key part would share the unpadded counter on SQL Server, which ignores trailing spaces when
        // comparing keys, so every provider refuses it before a statement runs.
        var paddedName = async () => await host.Generator.NextAsync(name + " ", cancellationToken: AbortToken);
        var paddedPartition = async () => await host.Generator.NextAsync(name, "2026 ", AbortToken);

        await tooLongName.Should().ThrowAsync<ArgumentException>();
        await blankPartition.Should().ThrowAsync<ArgumentException>();
        await tooLongPartition.Should().ThrowAsync<ArgumentException>();
        await paddedName.Should().ThrowAsync<ArgumentException>();
        await paddedPartition.Should().ThrowAsync<ArgumentException>();

        using (host.CurrentTenant.Change(new string('t', SequenceFieldLimits.TenantIdMaxLength + 1)))
        {
            var tooLongTenant = async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken);

            await tooLongTenant.Should().ThrowAsync<ArgumentException>();
        }

        using (host.CurrentTenant.Change("acme "))
        {
            var paddedTenant = async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken);

            await paddedTenant.Should().ThrowAsync<ArgumentException>();
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_accept_key_parts_at_their_maximum_lengths()
    {
        var name = CreateName("max").PadRight(SequenceFieldLimits.NameMaxLength, 'n');
        var partition = new string('p', SequenceFieldLimits.PartitionMaxLength);
        var tenant = new string('t', SequenceFieldLimits.TenantIdMaxLength);
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);

        using (host.CurrentTenant.Change(tenant))
        {
            (await host.Generator.NextAsync(name, partition, AbortToken)).Should().Be(1);
        }

        (await Fixture.ReadValueAsync(new SequenceKey(tenant, name, partition), AbortToken)).Should().Be(1);
    }

    #endregion

    #region Gap-free mode

    public virtual async Task should_give_the_next_unit_the_number_a_rolled_back_unit_took()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);

        // A committed first number puts a row in place, so a unit that ran its increment autonomously would move
        // the stored value and the next unit would receive 3 instead of 2.
        await using (var first = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            (await first.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
            await first.CommitAsync(AbortToken);
        }

        await using (var unitA = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            (await unitA.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(2);
            await unitA.RollbackAsync();
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);

        await using (var unitB = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            (await unitB.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(2);
            await unitB.CommitAsync(AbortToken);
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(2);
    }

    public virtual async Task should_block_the_second_unit_until_the_first_commits()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);

        await using (var seed = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            await seed.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);
            await seed.CommitAsync(AbortToken);
        }

        await using var unitA = await Fixture.BeginUnitAsync(host, AbortToken);
        await using var unitB = await Fixture.BeginUnitAsync(host, AbortToken);
        var taken = await unitA.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);

        var pending = unitB.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken).AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(SequencesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "the second unit waits on the row the first unit holds");

        await unitA.CommitAsync(AbortToken);
        var next = await pending.WaitAsync(SequencesFixtureExtensions.ReleaseTimeout, AbortToken);
        await unitB.CommitAsync(AbortToken);

        taken.Should().Be(2);
        next.Should().Be(taken + 1);
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(taken + 1);
    }

    public virtual async Task should_hand_the_waiting_unit_the_number_of_a_unit_that_rolls_back_on_a_new_key()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);
        await using var unitA = await Fixture.BeginUnitAsync(host, AbortToken);
        await using var unitB = await Fixture.BeginUnitAsync(host, AbortToken);

        var taken = await unitA.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);
        var pending = unitB.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken).AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(SequencesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "first use of a key the first unit is creating waits for that unit");

        await unitA.RollbackAsync();
        var next = await pending.WaitAsync(SequencesFixtureExtensions.ReleaseTimeout, AbortToken);
        await unitB.CommitAsync(AbortToken);

        taken.Should().Be(1);
        next.Should().Be(1, "the rolled-back unit's number is handed out again");
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);
    }

    public virtual async Task should_return_consecutive_values_for_sequential_calls_in_one_unit()
    {
        var name = CreateName("invoice");
        await using var host = await Fixture.CreateHostAsync(
            setup =>
                setup.Policy(
                    name,
                    new SequencePolicy
                    {
                        Start = 100,
                        Step = 2,
                        Mode = SequenceMode.GapFree,
                    }
                ),
            AbortToken
        );
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(100);
        (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(102);
        (await unit.Unit.Sequences.NextAsync(name, "2026", AbortToken)).Should().Be(100);
        await unit.CommitAsync(AbortToken);

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(102);
        (await Fixture.ReadValueAsync(new SequenceKey("", name, "2026"), AbortToken)).Should().Be(100);
    }

    public virtual async Task should_key_gap_free_counters_by_the_current_tenant()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        using (host.CurrentTenant.Change("tenant-a"))
        {
            (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
            (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(2);
        }

        using (host.CurrentTenant.Change("tenant-b"))
        {
            (await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
        }

        await unit.CommitAsync(AbortToken);

        (await Fixture.ReadValueAsync(new SequenceKey("tenant-a", name, ""), AbortToken)).Should().Be(2);
        (await Fixture.ReadValueAsync(new SequenceKey("tenant-b", name, ""), AbortToken)).Should().Be(1);
    }

    public virtual async Task should_keep_an_owned_unit_replayable_after_a_gap_free_call()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);

        unit.Unit.IsRetryPrevented.Should()
            .BeFalse("replaying an owned unit rolls the increment back and takes it again");
        await unit.CommitAsync(AbortToken);
    }

    public virtual async Task should_mark_an_observed_unit_non_retryable_and_commit_with_its_transaction()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);

        await using (var observed = await Fixture.EnlistUnitAsync(host, AbortToken))
        {
            (await observed.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
            observed.Unit.IsRetryPrevented.Should().BeTrue();
            await observed.CommitAsync(AbortToken);
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);

        await using (var rolledBack = await Fixture.EnlistUnitAsync(host, AbortToken))
        {
            (await rolledBack.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(2);
            await rolledBack.RollbackAsync();
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);
    }

    public virtual async Task should_refuse_a_unit_on_another_database_before_any_statement()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);

        await using (var owned = await Fixture.BeginUnitOnOtherDatabaseAsync(host, AbortToken))
        {
            var act = async () => await owned.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database*");
            owned.Unit.State.Should().Be(UnitOfWorkState.Active, "a refusal leaves the unit usable");
        }

        await using (var observed = await Fixture.EnlistUnitOnOtherDatabaseAsync(host, AbortToken))
        {
            var act = async () => await observed.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database*");
            observed.Unit.IsRetryPrevented.Should().BeFalse("a refused call never marks the unit");
        }

        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_refuse_a_unit_that_already_completed()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);
        var sequences = unit.Unit.Sequences;
        await unit.CommitAsync(AbortToken);

        var act = async () => await sequences.NextAsync(name, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_refuse_a_gap_free_counter_through_the_injected_generator()
    {
        var name = CreateName("invoice");
        await using var host = await CreateGapFreeHostAsync(name);

        await using (var unit = await Fixture.BeginUnitAsync(host, AbortToken))
        {
            await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);
            await unit.CommitAsync(AbortToken);
        }

        var next = async () => await host.Generator.NextAsync(name, cancellationToken: AbortToken);
        var reserve = async () => await host.Generator.ReserveAsync(name, 5, cancellationToken: AbortToken);

        await next.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unit.Sequences*");
        await reserve.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unit.Sequences*");
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(1);
    }

    public virtual async Task should_refuse_a_fast_counter_through_the_unit()
    {
        var name = CreateName("receipt");
        await using var host = await Fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await Fixture.BeginUnitAsync(host, AbortToken);

        var act = async () => await unit.Unit.Sequences.NextAsync(name, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISequenceGenerator*");
        unit.Unit.State.Should().Be(UnitOfWorkState.Active);
        unit.Unit.IsRetryPrevented.Should().BeFalse();
        await unit.CommitAsync(AbortToken);
        (await Fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().BeNull();
    }

    #endregion

    /// <summary>Builds a host on which <paramref name="name" /> is registered as a gap-free counter.</summary>
    protected ValueTask<SequencesHost> CreateGapFreeHostAsync(string name)
    {
        return Fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Mode = SequenceMode.GapFree }),
            AbortToken
        );
    }

    /// <summary>Returns a counter name no other test uses.</summary>
    protected static string CreateName(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }
}
