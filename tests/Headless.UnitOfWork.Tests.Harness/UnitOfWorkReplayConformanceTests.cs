// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The replay and refusal policy every <c>RunAsync</c> spelling shares, asserted identically per provider: a
/// fault before the commit replays the whole block with a fresh unit (only on a spelling that replays), a fault
/// raised by the commit never replays, <see cref="IUnitOfWork.PreventRetry" /> ends replay, a drain fault after a
/// durable commit returns the block's result, and a joined block never replays on its own.
/// </summary>
public abstract class UnitOfWorkReplayConformanceTests(IUnitOfWorkReplayFixture fixture) : TestBase
{
    [Fact]
    public virtual async Task should_replay_a_fault_before_the_commit_only_when_the_spelling_replays()
    {
        await fixture.ResetAsync(AbortToken);

        var attempts = new List<IUnitOfWork>();
        var drains = 0;
        UnitOfWorkFailure? firstAttemptFailure = null;
        var attemptsWhenFirstFailed = 0;

        var act = () =>
            fixture.RunAsync(
                async (context, ct) =>
                {
                    attempts.Add(context.UnitOfWork);
                    context.UnitOfWork.OnCompleted(() =>
                    {
                        drains++;

                        return ValueTask.CompletedTask;
                    });
                    await context.InsertProbeRowAsync($"attempt-{attempts.Count}", ct);

                    if (attempts.Count == 1)
                    {
                        context.UnitOfWork.OnFailed(failure =>
                        {
                            firstAttemptFailure = failure;
                            attemptsWhenFirstFailed = attempts.Count;

                            return ValueTask.CompletedTask;
                        });

                        throw new ReplayableFaultException();
                    }

                    return 42;
                },
                AbortToken
            );

        if (fixture.ReplaysBeforeCommit)
        {
            (await act()).Should().Be(42);
            attempts.Should().HaveCount(2, "the fault before the commit replays the block once");
            attempts[1].Should().NotBeSameAs(attempts[0], "each attempt runs on a fresh unit");
            drains.Should().Be(1, "the abandoned attempt's completion work is dropped with its unit");
        }
        else
        {
            await act.Should().ThrowAsync<ReplayableFaultException>();
            attempts.Should().HaveCount(1, "this spelling never replays");
            drains.Should().Be(0);
        }

        firstAttemptFailure.Should().NotBeNull("the faulted attempt's unit is rolled back");
        firstAttemptFailure!
            .Reason.Should()
            .Be(UnitOfWorkFailureReason.RolledBack, "every spelling rolls a faulted attempt back explicitly");
        attemptsWhenFirstFailed.Should().Be(1, "the rollback finishes before a replay begins");
        (await fixture.CountProbeRowsAsync(AbortToken))
            .Should()
            .Be(attempts.Count - 1, "the faulted attempt's row rolls back and only a replay's row commits");
    }

    [Fact]
    public virtual async Task should_not_replay_when_the_commit_faults()
    {
        await fixture.ResetAsync(AbortToken);

        var attempts = 0;
        IUnitOfWork? unit = null;
        UnitOfWorkFailure? failure = null;

        var act = () =>
            fixture.RunAsync(
                async (context, ct) =>
                {
                    attempts++;
                    unit = context.UnitOfWork;
                    context.UnitOfWork.OnFailed(f =>
                    {
                        failure = f;

                        return ValueTask.CompletedTask;
                    });
                    await context.InsertProbeRowAsync("commit-faulted", ct);
                    await context.ArmCommitFaultAsync(ct);

                    return 1;
                },
                AbortToken
            );

        var thrown = await act.Should().ThrowAsync<Exception>();
        attempts.Should().Be(1, "a fault raised once the commit started must never replay the block");
        unit!.State.Should().Be(UnitOfWorkState.Failed);
        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Faulted);
        failure.Exception.Should().BeSameAs(thrown.Which, "the caller receives the commit's own fault");
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the faulted commit made nothing durable");
    }

    [Fact]
    public virtual async Task should_not_replay_after_the_block_prevents_retry()
    {
        await fixture.ResetAsync(AbortToken);

        var attempts = 0;

        var act = () =>
            fixture.RunAsync<int>(
                async (context, ct) =>
                {
                    attempts++;
                    await context.InsertProbeRowAsync("retry-prevented", ct);
                    context.UnitOfWork.PreventRetry();

                    throw new ReplayableFaultException();
                },
                AbortToken
            );

        await act.Should().ThrowAsync<ReplayableFaultException>();
        attempts.Should().Be(1, "PreventRetry keeps the fault away from the replay loop");
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public virtual async Task should_return_the_result_when_the_drain_faults_after_a_durable_commit()
    {
        await fixture.ResetAsync(AbortToken);

        var attempts = 0;

        var result = await fixture.RunAsync(
            async (context, ct) =>
            {
                attempts++;
                context.UnitOfWork.OnCompleted(() => throw new ReplayableFaultException("drain down"));
                await context.InsertProbeRowAsync("durable", ct);

                return 7;
            },
            AbortToken
        );

        result.Should().Be(7, "surfacing a post-commit drain fault would invite a retry that double-applies");
        attempts.Should().Be(1);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public virtual async Task should_replay_a_joined_block_only_with_its_owner()
    {
        await fixture.ResetAsync(AbortToken);

        var ownerAttempts = 0;
        var joinedAttempts = 0;
        var joinedUnits = new List<IUnitOfWork>();
        var ownerUnits = new List<IUnitOfWork>();

        var act = () =>
            fixture.RunAsync(
                async (context, ct) =>
                {
                    ownerAttempts++;
                    ownerUnits.Add(context.UnitOfWork);

                    await context.RunJoinedAsync(
                        async (joined, joinedCt) =>
                        {
                            joinedAttempts++;
                            joinedUnits.Add(joined);
                            await context.InsertProbeRowAsync($"joined-{joinedAttempts}", joinedCt);

                            if (joinedAttempts == 1)
                            {
                                throw new ReplayableFaultException();
                            }
                        },
                        ct
                    );

                    return 3;
                },
                AbortToken
            );

        if (fixture.ReplaysBeforeCommit)
        {
            (await act()).Should().Be(3);
        }
        else
        {
            await act.Should().ThrowAsync<ReplayableFaultException>();
        }

        joinedAttempts
            .Should()
            .Be(ownerAttempts, "a joined block runs once per owner attempt and never replays on its own");
        joinedUnits.Should().Equal(ownerUnits, "each joined run receives its owner's unit");
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(fixture.ReplaysBeforeCommit ? 1 : 0);
    }
}
