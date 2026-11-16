// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// Provider-agnostic acceptance scenarios for the single-call <c>RunAsync</c> helpers: a completed unit drains
/// its <c>OnCompleted</c> work after the rows are durable; a throwing operation rolls the rows back, discards the
/// completion work, runs the failure work, and surfaces the operation's own exception. The drain is observed
/// through an awaited <see cref="TaskCompletionSource" /> with a failsafe timeout, never an immediately-read flag.
/// </summary>
public abstract class UnitOfWorkRunConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IUnitOfWorkRunFixture
{
    [Fact]
    public virtual async Task should_drain_completion_work_and_persist_rows_when_operation_completes()
    {
        await fixture.ResetAsync(AbortToken);

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await fixture.RunAsync(
            async (context, ct) =>
            {
                context.UnitOfWork.OnCompleted(() =>
                {
                    drained.TrySetResult();

                    return ValueTask.CompletedTask;
                });

                await context.InsertProbeRowAsync("completed", ct);
            },
            AbortToken
        );

        await drained.Task.Bounded();

        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "the committed probe row must be durable");
    }

    [Fact]
    public virtual async Task should_discard_completion_work_and_roll_back_rows_when_operation_throws()
    {
        await fixture.ResetAsync(AbortToken);

        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UnitOfWorkFailure? failure = null;
        InvalidOperationException? thrown = null;

        try
        {
            await fixture.RunAsync(
                async (context, ct) =>
                {
                    context.UnitOfWork.OnCompleted(() =>
                    {
                        drained.TrySetResult();

                        return ValueTask.CompletedTask;
                    });
                    context.UnitOfWork.OnFailed(f =>
                    {
                        failure = f;

                        return ValueTask.CompletedTask;
                    });
                    // Scope-local state is disposed on both outcomes after the callbacks, so its disposal marks the
                    // moment the failure drain has finished and the "nothing ran" read below is meaningful.
                    context.UnitOfWork.GetOrAdd(_ => new DrainSentinel(settled));

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
        await settled.Task.Bounded();
        drained.Task.IsCompleted.Should().BeFalse("a rolled-back unit of work must discard its OnCompleted work");
        failure.Should().NotBeNull("a rolled-back unit of work must run its OnFailed work");
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.RolledBack);
    }

    private sealed class DrainSentinel(TaskCompletionSource settled) : IDisposable
    {
        public void Dispose()
        {
            settled.TrySetResult();
        }
    }
}
