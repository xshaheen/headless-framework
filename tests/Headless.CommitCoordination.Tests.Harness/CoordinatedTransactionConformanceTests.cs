// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Provider-agnostic acceptance scenarios for the single-call <c>ExecuteCoordinatedTransactionAsync</c>
/// helpers: the same observable contract must hold whether the commit edge is detected by the EF
/// interceptor (awaited in <c>CommitAsync</c>), the SqlClient diagnostic (off-thread drain), or driven
/// inline by the PostgreSQL helper. Scenarios therefore observe the commit drain through an awaited
/// <see cref="TaskCompletionSource" /> with a failsafe timeout — never an immediately-read flag.
/// Un-signalled <em>external</em> commits are intentionally NOT conformance scenarios: they are
/// provider-specific by design (PostgreSQL is inline/caller-driven).
/// </summary>
public abstract class CoordinatedTransactionConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, ICoordinatedTransactionFixture
{
    private static readonly TimeSpan _DrainTimeout = TimeSpan.FromSeconds(15);

    public virtual async Task should_drain_buffered_commit_work_and_persist_rows_when_operation_commits()
    {
        await fixture.ResetAsync(AbortToken);

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await fixture.RunCoordinatedAsync(
            async (context, ct) =>
            {
                context.Coordinator.OnCommit(
                    (_, _) =>
                    {
                        drained.TrySetResult();

                        return ValueTask.CompletedTask;
                    }
                );

                await context.InsertProbeRowAsync("committed", ct);
            },
            AbortToken
        );

        var winner = await Task.WhenAny(drained.Task, Task.Delay(_DrainTimeout, AbortToken));
        winner
            .Should()
            .BeSameAs(drained.Task, "a committed coordinated transaction must drain buffered OnCommit work");

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "the committed probe row must be durable");
    }

    public virtual async Task should_discard_buffered_commit_work_and_roll_back_rows_when_operation_throws()
    {
        await fixture.ResetAsync(AbortToken);

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        InvalidOperationException? thrown = null;

        try
        {
            await fixture.RunCoordinatedAsync(
                async (context, ct) =>
                {
                    context.Coordinator.OnCommit(
                        (_, _) =>
                        {
                            drained.TrySetResult();

                            return ValueTask.CompletedTask;
                        }
                    );

                    await context.InsertProbeRowAsync("rolled-back", ct);

                    throw new InvalidOperationException("conformance-rollback");
                },
                AbortToken
            );
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull("the helper must rethrow the operation's exception");
        thrown!.Message.Should().Be("conformance-rollback");

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(0, "the rolled-back probe row must not be durable");
        drained.Task.IsCompleted.Should().BeFalse("a rolled-back coordinated transaction must discard buffered OnCommit work");
    }
}
