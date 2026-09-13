// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;

namespace Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async

/// <summary>
/// In-memory scenarios that need a captured synchronization context — a concern the portable
/// <see cref="ICommitCoordinationFixture" /> can't supply, so they run directly against the factory.
/// </summary>
public sealed class InMemoryCommitCoordinationSpecificTests
{
    [Fact]
    public void should_complete_commit_drain_without_deadlock_under_single_threaded_synchronization_context()
    {
        var original = SynchronizationContext.Current;
        using var context = new SingleThreadedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var completed = false;

            context.Run(async () =>
            {
                var factory = new CommitScopeFactory(new CommitScopeStack());

                await using var scope = factory.Open(relational: null);

                scope.Coordinator.OnCommit(async () =>
                {
                    // Force a real continuation back onto the captured context.
                    await Task.Yield();
                    await Task.Delay(1);
                });

                await scope.SignalAsync(CommitOutcome.Committed);
                completed = true;
            });

            completed.Should().BeTrue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    /// <summary>Minimal pumping single-threaded synchronization context for deadlock regression guards.</summary>
    private sealed class SingleThreadedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public void Run(Func<Task> work)
        {
            var rootTask = work();
            _ = rootTask.ContinueWith(
                _ => _queue.CompleteAdding(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }

            // Surface any fault (including a deadlock-induced timeout if the test runner cancels).
            rootTask.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _queue.Dispose();
        }
    }
}
