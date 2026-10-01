// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The replay and refusal policy of <see cref="UnitOfWorkRunner.RunAsync{TResult}" /> over fake resources and a
/// fake replay loop, so the rules every provider shares are pinned without a database: the Docker suites prove
/// the same outcomes per provider.
/// </summary>
public sealed class UnitOfWorkRunnerTests : TestBase
{
    private sealed class TransientFaultException() : Exception("Simulated transient failure.");

    [Fact]
    public async Task should_roll_back_a_faulted_attempt_before_replaying_it_on_a_fresh_unit()
    {
        var run = new Run();
        var attempts = new List<IUnitOfWork>();
        UnitOfWorkFailure? firstFailure = null;
        var firstResourceRolledBackWhenSecondBegan = false;

        var result = await run.ExecuteAsync(
            (unit, _) =>
            {
                attempts.Add(unit);

                if (attempts.Count == 1)
                {
                    unit.OnFailed(failure =>
                    {
                        firstFailure = failure;

                        return ValueTask.CompletedTask;
                    });

                    return Task.FromException<int>(new TransientFaultException());
                }

                firstResourceRolledBackWhenSecondBegan = run.Resources[0].RollbackCalls == 1;

                return Task.FromResult(attempts.Count);
            },
            new ReplayOnceStrategy(),
            AbortToken
        );

        result.Should().Be(2);
        attempts[1].Should().NotBeSameAs(attempts[0], "a replay runs on a fresh unit");
        firstFailure!
            .Reason.Should()
            .Be(UnitOfWorkFailureReason.RolledBack, "the runner rolls a faulted attempt back explicitly");
        firstResourceRolledBackWhenSecondBegan.Should().BeTrue("the rollback finishes before the replay begins");
        run.Resources[1].CommitCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_replay_when_the_begin_itself_faults()
    {
        var run = new Run { BeginFaults = 1 };
        var attempts = 0;

        var result = await run.ExecuteAsync(
            (_, _) => Task.FromResult(++attempts),
            new ReplayOnceStrategy(),
            AbortToken
        );

        result.Should().Be(1, "the block ran once: the first attempt never reached it");
        run.Resources.Should().ContainSingle().Which.CommitCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_when_the_block_completed_its_own_unit_and_then_threw()
    {
        var run = new Run();
        var attempts = 0;

        var act = () =>
            run.ExecuteAsync(
                async (unit, ct) =>
                {
                    attempts++;
                    await unit.CompleteAsync(ct);

                    throw new TransientFaultException();
                },
                new ReplayOnceStrategy(),
                AbortToken
            );

        await act.Should().ThrowAsync<TransientFaultException>();
        attempts.Should().Be(1, "the block's own commit is durable, so a replay would apply it twice");
        run.Resources.Should().ContainSingle().Which.CommitCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_return_the_result_and_warn_when_the_block_completed_its_own_unit()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();
        var run = new Run(logger);

        var result = await run.ExecuteAsync(
            async (unit, ct) =>
            {
                await unit.CompleteAsync(ct);

                return 7;
            },
            new ReplayOnceStrategy(),
            AbortToken
        );

        result.Should().Be(7);
        run.Resources.Should().ContainSingle().Which.CommitCalls.Should().Be(1, "the runner does not commit twice");
        logger.Entries.Should().ContainSingle(entry => entry.EventId.Id == 12 && entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task should_refuse_a_block_that_rolled_back_its_own_unit_and_returned()
    {
        var run = new Run();
        var attempts = 0;

        var act = () =>
            run.ExecuteAsync(
                async (unit, _) =>
                {
                    attempts++;
                    await unit.RollbackAsync();

                    return 1;
                },
                new ReplayOnceStrategy(),
                AbortToken
            );

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be(UnitOfWorkRunner.OwnedBlockEndedUnitMessage);
        attempts.Should().Be(1, "the block's own verb decided the outcome, so it is never replayed");
    }

    [Fact]
    public async Task should_log_a_rollback_fault_and_surface_the_operations_own_exception()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();
        var run = new Run(logger) { RollbackFault = new InvalidOperationException("rollback down") };

        var act = () =>
            run.ExecuteAsync(
                (_, _) => Task.FromException<int>(new TransientFaultException()),
                NoReplayUnitOfWorkExecutionStrategy.Instance,
                AbortToken
            );

        await act.Should().ThrowAsync<TransientFaultException>();
        logger.Entries.Should().ContainSingle(entry => entry.EventId.Id == 10 && entry.Level == LogLevel.Warning);
    }

    /// <summary>Replays the attempt once, on any fault: the runner's refusal rules must hold against it.</summary>
    private sealed class ReplayOnceStrategy : IUnitOfWorkExecutionStrategy
    {
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> attempt,
            CancellationToken cancellationToken
        )
        {
#pragma warning disable ERP022 // Observing the first fault would make the double worse: an execution strategy discards it and replays.
            try
            {
                return await attempt(cancellationToken);
            }
            catch (Exception)
            {
                return await attempt(cancellationToken);
            }
#pragma warning restore ERP022
        }
    }

    /// <summary>One owned RunAsync over fake resources, one per attempt.</summary>
    private sealed class Run(ILogger<UnitOfWorkFactory>? logger = null)
    {
        private readonly UnitOfWorkFactory _factory = new(logger);

        public List<FakeUnitOfWorkResource> Resources { get; } = [];

        public int BeginFaults { get; init; }

        public Exception? RollbackFault { get; init; }

        public Task<int> ExecuteAsync(
            Func<IUnitOfWork, CancellationToken, Task<int>> operation,
            IUnitOfWorkExecutionStrategy strategy,
            CancellationToken cancellationToken
        )
        {
            var beginFaults = BeginFaults;

            return UnitOfWorkRunner.RunAsync(
                static () => ValueTask.FromResult<IUnitOfWork?>(null),
                ct =>
                {
                    if (beginFaults-- > 0)
                    {
                        return ValueTask.FromException<IUnitOfWork>(new TransientFaultException());
                    }

                    var resource = new FakeUnitOfWorkResource { RollbackFault = RollbackFault };
                    Resources.Add(resource);

                    return _factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(resource), ct);
                },
                operation,
                strategy,
                (ILogger?)logger ?? NullLogger.Instance,
                cancellationToken
            );
        }
    }
}
