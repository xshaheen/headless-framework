// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TrackedCommitScopeTests
{
    [Fact]
    public async Task should_not_dispose_owned_services_on_redundant_signal_while_claiming_drain_is_in_flight()
    {
        // The SqlServer helper signals explicitly after the diagnostic already claimed the scope: the redundant
        // signal no-ops on the latch and must not tear down the owned DI scope under the winner's drain.
        using var ownedScope = new FakeServiceScope();
        await using var tracked = _CreateTrackedScope(ownedScope);

        var drainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? drainResolveFailure = null;

        tracked.Coordinator.OnCommit(
            async (context, _) =>
            {
                drainEntered.SetResult();
                await releaseDrain.Task;

                try
                {
                    context.Services.GetRequiredService<Marker>();
                }
                catch (Exception ex)
                {
                    drainResolveFailure = ex;
                }
            }
        );

        var claimingSignal = tracked.SignalAsync(CommitOutcome.Committed).AsTask();
        await drainEntered.Task;

        // The redundant signal must complete without waiting for (or tearing down) the in-flight drain.
        await tracked.SignalAsync(CommitOutcome.Committed);

        ownedScope.Disposed.Should().BeFalse("the claiming drain still owns the DI scope");

        releaseDrain.SetResult();
        await claimingSignal;

        drainResolveFailure.Should().BeNull("the drain callback resolves from the owned scope it owns");
        ownedScope.Disposed.Should().BeTrue("the claiming signal disposes the owned scope after its drain");
    }

    [Fact]
    public async Task should_keep_owned_services_alive_for_the_offloaded_rollback_drain_on_sync_unsignalled_dispose()
    {
        // Sync Dispose of an un-signalled scope offloads the rollback drain to the background; the drain (not the
        // disposing frame) owns the DI scope it resolves rollback callbacks from.
        using var ownedScope = new FakeServiceScope();
        var tracked = _CreateTrackedScope(ownedScope);

        var drainFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? drainResolveFailure = null;

        tracked.Coordinator.OnRollback(
            (context, _) =>
            {
                try
                {
                    context.Services.GetRequiredService<Marker>();
                }
                catch (Exception ex)
                {
                    drainResolveFailure = ex;
                }
                finally
                {
                    drainFinished.SetResult();
                }

                return ValueTask.CompletedTask;
            }
        );

#pragma warning disable VSTHRD103 // the synchronous Dispose path (offloaded drain) is the behavior under test
        // ReSharper disable once MethodHasAsyncOverload
        tracked.Dispose();
#pragma warning restore VSTHRD103

        await drainFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        drainResolveFailure.Should().BeNull("the offloaded rollback drain runs before the owned scope is disposed");

        // The abandon cleanup disposes the owned scope after the offloaded drain; wait for the transfer to land.
        await _WaitForDisposalAsync(ownedScope);
    }

    [Fact]
    public async Task should_dispose_owned_services_after_claiming_signal_even_when_disposed_concurrently()
    {
        // A DisposeAsync racing an in-flight claiming SignalAsync must not leak the owned scope: the dispose path
        // sees the started signal and defers; the claiming signal disposes after its drain.
        using var ownedScope = new FakeServiceScope();
        var tracked = _CreateTrackedScope(ownedScope);

        var drainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        tracked.Coordinator.OnCommit(
            async (_, _) =>
            {
                drainEntered.SetResult();
                await releaseDrain.Task;
            }
        );

        var claimingSignal = tracked.SignalAsync(CommitOutcome.Committed).AsTask();
        await drainEntered.Task;

        await tracked.DisposeAsync();

        ownedScope.Disposed.Should().BeFalse("the in-flight claiming signal still owns the DI scope");

        releaseDrain.SetResult();
        await claimingSignal;

        ownedScope.Disposed.Should().BeTrue("the claiming signal disposes after its drain");
    }

    private static TrackedCommitScope _CreateTrackedScope(FakeServiceScope ownedScope)
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var inner = factory.Begin(ownedScope.ServiceProvider);

        return new TrackedCommitScope(inner, static _ => { }, new AsyncServiceScope(ownedScope));
    }

    private static async Task _WaitForDisposalAsync(FakeServiceScope scope)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (scope.Disposed)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The owned service scope was not disposed after the offloaded drain.");
    }

    private sealed class Marker;

    private sealed class FakeServiceScope : IServiceScope, IServiceProvider
    {
        public bool Disposed { get; private set; }

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return serviceType == typeof(Marker) ? new Marker() : null;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
