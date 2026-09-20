// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable xUnit1051 // The dedicated-thread race bodies pass CancellationToken.None deliberately: the race must not abort with the test token.
#pragma warning disable MA0045 // Dedicated blocking threads in the race tests; async here would pool-hop and change the race.

namespace Tests;

public sealed class UnitOfWorkFactoryTests : TestBase
{
    private static (UnitOfWorkFactory Factory, CapturingLogger<UnitOfWorkFactory> Logger) Create()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();

        return (new UnitOfWorkFactory(logger), logger);
    }

    [Fact]
    public async Task should_open_independent_units_and_share_nothing_between_them()
    {
        // The factory is a singleton with no slot: two begins are two units, and a second resource is never
        // rejected because nothing is "already active" from the factory's point of view.
        var (factory, _) = Create();
        var first = new FakeUnitOfWorkResource();
        var second = new FakeUnitOfWorkResource();

        await using var one = await factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(first),
            options: null,
            AbortToken
        );
        await using var two = await factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(second),
            options: null,
            AbortToken
        );

        one.Should().NotBeSameAs(two);
        await two.CompleteAsync(AbortToken);
        await one.RollbackAsync();

        second.CommitCalls.Should().Be(1);
        second.RollbackCalls.Should().Be(0);
        first.CommitCalls.Should().Be(0);
        first.RollbackCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_open_units_concurrently_from_one_factory()
    {
        // No latch, no slot: concurrent begins on a shared singleton each get their own unit.
        var (factory, _) = Create();

        var units = await Task.WhenAll(
            Enumerable
                .Range(0, 64)
                .Select(_ =>
                    factory
                        .BeginAsync(
                            static _ => ValueTask.FromResult<IUnitOfWorkResource>(new FakeUnitOfWorkResource()),
                            options: null,
                            CancellationToken.None
                        )
                        .AsTask()
                )
        );

        units.Distinct().Should().HaveCount(64);
        units.Should().OnlyContain(unit => unit.State == UnitOfWorkState.Active);

        foreach (var unit in units)
        {
            await unit.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_propagate_a_faulting_resource_begin_as_is()
    {
        var (factory, _) = Create();

        var act = () =>
            factory
                .BeginAsync(
                    static _ => ValueTask.FromException<IUnitOfWorkResource>(new InvalidOperationException("boom")),
                    options: null,
                    AbortToken
                )
                .AsTask();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
    }

    [Fact]
    public async Task should_complete_or_dispose_exactly_once_when_two_threads_race_200_times()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var (factory, _) = Create();
            var resource = new FakeUnitOfWorkResource();
            var unitOfWork = await factory.BeginAsync(
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
    public async Task should_not_throw_from_an_implicit_dispose_when_the_rollback_faults_and_still_drain_on_failed()
    {
        var (factory, logger) = Create();
        var resource = new FakeUnitOfWorkResource { RollbackFault = new InvalidOperationException("rollback down") };
        var unitOfWork = await factory.BeginAsync(
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
        var (factory, _) = Create();
        var resource = new FakeUnitOfWorkResource { RollbackFault = new InvalidOperationException("rollback down") };
        await using var unitOfWork = await factory.BeginAsync(
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
        }
            .Should()
            .Equal(0, 1, 2, 3);

        new[] { (int)TransactionEnlistment.Optional, (int)TransactionEnlistment.Required }.Should().Equal(0, 1);
    }

    [Fact]
    public async Task should_log_a_failure_callback_fault_and_never_propagate_it()
    {
        var (factory, logger) = Create();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);

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
        var (factory, logger) = Create();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);
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
        var (factory, _) = Create();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);
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
        var (factory, _) = Create();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);
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
        var (factory, _) = Create();
        var unitOfWork = await factory.BeginAsync(cancellationToken: AbortToken);

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
