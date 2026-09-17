// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable xUnit1051 // The dedicated-thread race bodies pass CancellationToken.None deliberately: the race must not abort with the test token.
#pragma warning disable MA0045 // Dedicated blocking threads in the race tests; async here would pool-hop and change the race.

namespace Tests;

public sealed class UnitOfWorkManagerTests : TestBase
{
    private static (UnitOfWorkManager Manager, CapturingLogger<UnitOfWorkManager> Logger) Create()
    {
        var logger = new CapturingLogger<UnitOfWorkManager>();

        return (new UnitOfWorkManager(logger), logger);
    }

    [Fact]
    public async Task should_claim_the_slot_synchronously_before_the_first_await_of_a_resource_begin()
    {
        var (manager, _) = Create();
        var begun = new TaskCompletionSource<IUnitOfWorkResource>(TaskCreationOptions.RunContinuationsAsynchronously);

        var beginTask = manager.BeginAsync(
            async ct =>
            {
                await Task.Yield();

                return await begun.Task;
            },
            options: null,
            cancellationToken: AbortToken
        );

        // The factory has yielded; the slot must already be claimed (a concurrent begin throws now).
        var concurrent = () => manager.BeginAsync(cancellationToken: AbortToken).AsTask();
        await concurrent
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Await the first BeginAsync before beginning again*");

        begun.SetResult(new FakeUnitOfWorkResource());
        (await beginTask).State.Should().Be(UnitOfWorkState.Active);
    }

    [Fact]
    public async Task should_throw_the_concurrent_begin_message_for_a_concurrent_enlist()
    {
        var (manager, _) = Create();
        var begun = new TaskCompletionSource<IUnitOfWorkResource>(TaskCreationOptions.RunContinuationsAsynchronously);

        var inFlight = manager.BeginAsync(async ct => await begun.Task, options: null, cancellationToken: AbortToken);

        // Enlist while the resource begin is still in flight: the latch must reject it.
        manager
            .Invoking(m => m.Enlist(new FakeUnitOfWorkResource(isOwned: false)))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Await the first BeginAsync before beginning again*");

        begun.SetResult(new FakeUnitOfWorkResource());
        (await inFlight).State.Should().Be(UnitOfWorkState.Active);
    }

    [Fact]
    public async Task should_throw_the_concurrent_begin_message_when_a_begin_races_an_in_flight_begin()
    {
        var (manager, _) = Create();
        using var gate = new ManualResetEventSlim(false);
        using var inFlight = new ManualResetEventSlim(false);

        var first = Task.Run(
            async () =>
            {
                var begin = manager.BeginAsync(
                    async ct =>
                    {
                        inFlight.Set();

                        await Task.Run(() => gate.Wait(TimeSpan.FromSeconds(10)), AbortToken);

                        return new FakeUnitOfWorkResource();
                    },
                    options: null,
                    cancellationToken: CancellationToken.None
                );

                return await begin;
            },
            AbortToken
        );

        inFlight.Wait(TimeSpan.FromSeconds(10));

        var second = () => manager.BeginAsync(cancellationToken: AbortToken).AsTask();

        (await second.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*Await the first BeginAsync before beginning again, or run parallel work in separate service scopes.*"
        );

        gate.Set();
        var root = await first;

        root.State.Should().Be(UnitOfWorkState.Active);
        await root.DisposeAsync();
    }

    [Fact]
    public async Task should_never_yield_two_roots_when_two_begins_race_200_times()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var (manager, _) = Create();
            using var barrier = new Barrier(2);
            var firstResource = new FakeUnitOfWorkResource();
            var secondResource = new FakeUnitOfWorkResource();
            IUnitOfWork? first = null;
            IUnitOfWork? second = null;
            Exception? firstFault = null;
            Exception? secondFault = null;

            var tasks = new[]
            {
                Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            first = manager
                                .BeginAsync(
                                    _ => ValueTask.FromResult<IUnitOfWorkResource>(firstResource),
                                    options: null,
                                    cancellationToken: AbortToken
                                )
                                .GetAwaiter()
                                .GetResult();
                        }
                        catch (InvalidOperationException ex)
                        {
                            firstFault = ex;
                        }
                    },
                    AbortToken,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ),
                Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            second = manager
                                .BeginAsync(
                                    _ => ValueTask.FromResult<IUnitOfWorkResource>(secondResource),
                                    options: null,
                                    cancellationToken: AbortToken
                                )
                                .GetAwaiter()
                                .GetResult();
                        }
                        catch (InvalidOperationException ex)
                        {
                            secondFault = ex;
                        }
                    },
                    AbortToken,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ),
            };

            await Task.WhenAll(tasks);

            var roots = new[] { first, second }.Count(u => u is not null);

            roots.Should().Be(1, "exactly one begin may win the slot; the loser gets the catalogued message");
            (firstFault, secondFault).Should().NotBeNull();

            var winner = first ?? second;
            winner!.State.Should().Be(UnitOfWorkState.Active);
            manager.Current.Should().BeSameAs(winner);

            var loserFault = (first is null ? firstFault : secondFault)!;
            loserFault.Should().BeOfType<InvalidOperationException>();

            await winner.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_complete_or_dispose_exactly_once_when_two_threads_race_200_times()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var (manager, _) = Create();
            var resource = new FakeUnitOfWorkResource();
            var unitOfWork = await manager.BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
                options: null,
                cancellationToken: AbortToken
            );

            using var barrier = new Barrier(2);
            Exception? completeFault = null;
            Exception? disposeFault = null;

            var tasks = new[]
            {
                Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            unitOfWork.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            completeFault = ex;
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ),
                Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();

                        try
                        {
                            unitOfWork.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            disposeFault = ex;
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                ),
            };

            await Task.WhenAll(tasks);

            // Exactly one side won the terminal claim. CompleteAsync-after-terminal throws the catalogue
            // message; the dispose of an already-terminal unit is a no-op. Either way the unit ends in exactly
            // one terminal state and the resource saw exactly one commit or rollback — never both, never two.
            var terminalStates = new[]
            {
                unitOfWork.State == UnitOfWorkState.Completed,
                unitOfWork.State == UnitOfWorkState.Failed,
            }.Count(isTrue => isTrue);
            terminalStates.Should().Be(1, "exactly one side wins the terminal claim");
            (resource.CommitCalls + resource.RollbackCalls).Should().BeLessThanOrEqualTo(1);

            if (completeFault is null)
            {
                // The complete won the claim: the unit committed and never rolled back.
                unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
                resource.RollbackCalls.Should().Be(0);
            }

            (completeFault, disposeFault).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task should_adopt_swap_and_restore_the_slot()
    {
        var (manager, _) = Create();
        var (foreignManager, _) = Create();
        var foreign = await foreignManager.BeginAsync(cancellationToken: AbortToken);

        using (manager.Adopt(foreign))
        {
            manager.Current.Should().BeSameAs(foreign);
        }

        manager.Current.Should().BeNull("the adoption handle restores the slot it replaced");
        await foreign.DisposeAsync();
    }

    [Fact]
    public async Task should_adopt_reentrantly_when_the_slot_already_holds_that_unit()
    {
        var (manager, _) = Create();
        await using var current = await manager.BeginAsync(cancellationToken: AbortToken);

        using (manager.Adopt(current))
        {
            manager.Current.Should().BeSameAs(current, "adopting the current unit is a no-op");
        }

        manager.Current.Should().BeSameAs(current, "a re-entrant adoption restores the same unit");
    }

    [Fact]
    public async Task should_throw_the_bound_unit_message_when_adopting_over_a_different_active_unit()
    {
        var (manager, _) = Create();
        var (foreignManager, _) = Create();
        await using var current = await manager.BeginAsync(cancellationToken: AbortToken);
        var foreign = await foreignManager.BeginAsync(cancellationToken: AbortToken);

        var act = () => manager.Adopt(foreign);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*belongs to another scope, and this scope already has a different active unit of work*");

        await foreign.DisposeAsync();
    }

    [Fact]
    public async Task should_adopt_reentrantly_when_the_slot_holds_a_child_view_over_the_offered_root()
    {
        // BeginAsync(db) binds the root handle to the context; a nested resource-less BeginAsync() makes Current
        // the child view. A save then offers the bound root handle for adoption — the slot already coordinates
        // on that engine, so it must be a no-op, not the "different active unit" rejection.
        var (manager, _) = Create();
        var resource = new FakeUnitOfWorkResource();
        await using var root = await manager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            AbortToken
        );
        await using var child = await manager.BeginAsync(cancellationToken: AbortToken);
        manager.Current.Should().BeSameAs(child);

        using (manager.Adopt(root))
        {
            manager.Current.Should().BeSameAs(child, "a re-entrant adoption swaps nothing");
        }

        manager.Current.Should().BeSameAs(child);
        await child.CompleteAsync(AbortToken);

        // The mirror image: the bound handle is a (completed) child view while Current is back on the root.
        using (manager.Adopt(child))
        {
            manager.Current.Should().BeSameAs(root);
        }

        await root.CompleteAsync(AbortToken);
        resource.CommitCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_roll_back_the_rejected_resource_when_a_second_resource_begins_under_a_resource_bearing_unit()
    {
        var (manager, _) = Create();
        var first = new FakeUnitOfWorkResource();
        var second = new FakeUnitOfWorkResource();
        await using var unitOfWork = await manager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(first),
            options: null,
            AbortToken
        );

        var act = () =>
            manager
                .BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(second), options: null, AbortToken)
                .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already active on another resource*");
        second.RollbackCalls.Should().Be(1, "the factory began a transaction the manager refused to keep");
        first.RollbackCalls.Should().Be(0);
        manager.Current.Should().BeSameAs(unitOfWork, "the rejection leaves the active unit untouched");
    }

    [Fact]
    public async Task should_log_a_rollback_fault_from_the_rejected_second_resource_and_still_throw_the_rejection()
    {
        var (manager, logger) = Create();
        var second = new FakeUnitOfWorkResource { RollbackFault = new InvalidOperationException("rollback down") };
        await using var unitOfWork = await manager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(new FakeUnitOfWorkResource()),
            options: null,
            AbortToken
        );

        var act = () =>
            manager
                .BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(second), options: null, AbortToken)
                .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already active on another resource*");
        logger.Entries.Should().Contain(e => e.Message.Contains("rejected second-resource BeginAsync faulted"));
    }

    [Fact]
    public async Task should_not_throw_from_an_implicit_dispose_when_the_rollback_faults_and_still_drain_on_failed()
    {
        var (manager, logger) = Create();
        var resource = new FakeUnitOfWorkResource { RollbackFault = new InvalidOperationException("rollback down") };
        var unitOfWork = await manager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            AbortToken
        );
        UnitOfWorkFailure? observed = null;
        unitOfWork.OnFailed(failure =>
        {
            observed = failure;

            return ValueTask.CompletedTask;
        });

        var act = async () => await unitOfWork.DisposeAsync();

        await act.Should().NotThrowAsync("an await using must not replace the exception the caller is unwinding");
        resource.RollbackCalls.Should().Be(1);
        observed.Should().NotBeNull("a rollback fault must not skip the OnFailed drain");
        observed!.Reason.Should().Be(UnitOfWorkFailureReason.Abandoned);
        logger.Entries.Should().Contain(e => e.Message.Contains("Rolling back an abandoned unit of work faulted"));
    }

    [Fact]
    public async Task should_surface_the_rollback_fault_from_an_explicit_rollback_and_still_drain_on_failed()
    {
        var (manager, _) = Create();
        var resource = new FakeUnitOfWorkResource { RollbackFault = new InvalidOperationException("rollback down") };
        await using var unitOfWork = await manager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            AbortToken
        );
        UnitOfWorkFailure? observed = null;
        unitOfWork.OnFailed(failure =>
        {
            observed = failure;

            return ValueTask.CompletedTask;
        });

        var act = async () => await unitOfWork.RollbackAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("rollback down");
        observed.Should().NotBeNull("the explicit verb surfaces the fault but the drain still runs first");
        observed!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
        unitOfWork.State.Should().Be(UnitOfWorkState.Failed);
    }

    [Fact]
    public async Task should_join_an_adopted_unit_as_a_child_when_beginning_on_the_same_resource()
    {
        var (manager, _) = Create();
        var (foreignManager, _) = Create();
        var resource = new FakeUnitOfWorkResource();
        var foreign = await foreignManager.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
            options: null,
            AbortToken
        );

        using (manager.Adopt(foreign))
        {
            var child = await manager.BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(resource),
                options: null,
                AbortToken
            );

            child.Should().NotBeSameAs(foreign, "a same-resource begin under an adopted unit opens a child view");
            child.Resource.Should().BeSameAs(resource);
            manager.Current.Should().BeSameAs(child);

            var enlisted = manager.Enlist(resource);
            enlisted.Should().NotBeSameAs(foreign);
            await enlisted.CompleteAsync(AbortToken);

            await child.CompleteAsync(AbortToken);
            manager.Current.Should().BeSameAs(foreign, "completing the child restores the adopted unit");
        }

        manager.Current.Should().BeNull();
        foreign.State.Should().Be(UnitOfWorkState.Active, "adoption never completes or fails the foreign unit");
        await foreign.CompleteAsync(AbortToken);
        resource.CommitCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_not_claim_an_adopted_unit_as_leaked_when_the_adopting_manager_is_disposed()
    {
        var (manager, logger) = Create();
        var (foreignManager, _) = Create();
        var foreign = await foreignManager.BeginAsync(cancellationToken: AbortToken);

        _ = manager.Adopt(foreign);
        await manager.DisposeAsync();

        foreign.State.Should().Be(UnitOfWorkState.Active, "the foreign scope owns the unit's lifecycle");
        logger.Entries.Should().NotContain(e => e.Message.Contains("still active when its service scope was disposed"));
        await foreign.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_the_concurrent_begin_message_when_adopting_while_a_begin_is_in_flight()
    {
        var (manager, _) = Create();
        var (foreignManager, _) = Create();
        var foreign = await foreignManager.BeginAsync(cancellationToken: AbortToken);
        var begun = new TaskCompletionSource<IUnitOfWorkResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = manager.BeginAsync(
            async ct =>
            {
                await Task.Yield();

                return await begun.Task;
            },
            options: null,
            AbortToken
        );

        var act = () => manager.Adopt(foreign);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Await the first BeginAsync before beginning again*");

        begun.SetResult(new FakeUnitOfWorkResource());
        await (await inFlight).DisposeAsync();
        await foreign.DisposeAsync();
    }

    [Fact]
    public async Task should_throw_when_adopting_null()
    {
        var (manager, _) = Create();

        var act = () => manager.Adopt(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("unitOfWork");
    }

    [Fact]
    public async Task should_throw_object_disposed_when_a_unit_outlives_its_manager()
    {
        var (manager, _) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);

        await manager.DisposeAsync();

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_dispose_manager_disposal_repeatedly()
    {
        var (manager, _) = Create();

        await manager.DisposeAsync();
        await manager.DisposeAsync();
    }

    [Fact]
    public void should_keep_unit_of_work_enum_numeric_contracts_stable()
    {
        new[] { (int)UnitOfWorkState.Active, (int)UnitOfWorkState.Completed, (int)UnitOfWorkState.Failed }
            .Should()
            .Equal(0, 1, 2);

        new[]
        {
            (int)UnitOfWorkFailureReason.Unspecified,
            (int)UnitOfWorkFailureReason.RolledBack,
            (int)UnitOfWorkFailureReason.Abandoned,
            (int)UnitOfWorkFailureReason.Faulted,
            (int)UnitOfWorkFailureReason.ScopeDisposed,
            (int)UnitOfWorkFailureReason.ChildAbandoned,
        }
            .Should()
            .Equal(0, 1, 2, 3, 4, 5);

        new[]
        {
            (int)TransactionEnlistment.WhenAvailable,
            (int)TransactionEnlistment.Required,
            (int)TransactionEnlistment.Never,
        }
            .Should()
            .Equal(0, 1, 2);
    }

    [Fact]
    public async Task should_log_a_failure_callback_fault_and_never_propagate_it()
    {
        var (manager, logger) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);

        unitOfWork.OnFailed(_ => throw new InvalidOperationException("failure callback fault"));

        var act = () => unitOfWork.RollbackAsync().AsTask();

        await act.Should().NotThrowAsync("an OnFailed fault is logged, never propagated");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("failure callback fault");
    }

    [Fact]
    public async Task should_log_and_continue_when_multiple_failure_callbacks_fault()
    {
        var (manager, logger) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);
        var ran = new List<int>();

        unitOfWork.OnFailed(_ =>
        {
            ran.Add(1);

            throw new InvalidOperationException("first fault");
        });
        unitOfWork.OnFailed(_ =>
        {
            ran.Add(2);

            return ValueTask.CompletedTask;
        });

        await unitOfWork.RollbackAsync();

        // A faulting OnFailed callback does not stop the remaining ones.
        ran.Should().Equal(1, 2);
        logger.Entries.Should().Contain(e => e.Message.Contains("first fault"));
    }

    [Fact]
    public async Task should_deregister_callbacks_when_their_handles_are_disposed_while_active()
    {
        var (manager, _) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);
        var completedCalls = 0;
        var failedCalls = 0;

        using (
            unitOfWork.OnCompleted(() =>
            {
                completedCalls++;

                return ValueTask.CompletedTask;
            })
        ) { }
        using (
            unitOfWork.OnFailed(_ =>
            {
                failedCalls++;

                return ValueTask.CompletedTask;
            })
        ) { }

        await unitOfWork.RollbackAsync();

        completedCalls.Should().Be(0);
        failedCalls.Should().Be(0);
    }

    [Fact]
    public async Task should_pass_arg_to_get_or_add_factory_and_reuse_existing_state()
    {
        var (manager, _) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);
        var calls = 0;

        var first = unitOfWork.GetOrAdd(
            new FactoryArg("first", () => ++calls),
            static (_, arg) => new StatefulEntry(arg.Value, arg.Next())
        );
        var second = unitOfWork.GetOrAdd(
            new FactoryArg("second", () => ++calls),
            static (_, arg) => new StatefulEntry(arg.Value, arg.Next())
        );

        first.Should().BeSameAs(second);
        first.State.Should().Be("first");
        first.CallsAtCreation.Should().Be(1);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_aggregate_multiple_completion_callback_faults()
    {
        var (manager, _) = Create();
        var unitOfWork = await manager.BeginAsync(cancellationToken: AbortToken);

        unitOfWork.OnCompleted(() => throw new InvalidOperationException("first"));
        unitOfWork.OnCompleted(() => throw new NotSupportedException("second"));

        var act = () => unitOfWork.CompleteAsync(AbortToken).AsTask();

        var exception = await act.Should().ThrowAsync<AggregateException>();
        exception.Which.InnerExceptions.Should().HaveCount(2);
        unitOfWork.State.Should().Be(UnitOfWorkState.Completed);
    }

    private sealed record FactoryArg(string Value, Func<int> Next);

    private sealed record StatefulEntry(string State, int CallsAtCreation);
}
