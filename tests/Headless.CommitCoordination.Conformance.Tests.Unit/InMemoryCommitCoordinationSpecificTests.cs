// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.CommitCoordination;
using Headless.CommitCoordination.DurableWork;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async

/// <summary>
/// In-memory scenarios that need a captured logger, attached capabilities, durable buffers, or a
/// synchronization context — concerns the portable <see cref="ICommitCoordinationFixture" /> can't
/// supply, so they run directly against the coordinator/factory.
/// </summary>
public sealed class InMemoryCommitCoordinationSpecificTests
{
    private static readonly IServiceProvider _Services = new EmptyServiceProvider();

    [Fact]
    public async Task should_drain_once_and_log_warning_when_terminal_signals_race()
    {
        // The factory propagates its logger to every new root coordinator, so the racing-signal
        // warning is observable through the public scope API without touching internals. A child
        // rollback drains the root first; the later root commit is the ignored, logged no-op.
        var logger = new CapturingLogger<CommitCoordinator>();
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack, logger);

        await using var root = factory.Begin(_Services);
        var rootCalls = 0;

        root.Coordinator.OnCommit(
            (_, _) =>
            {
                rootCalls++;

                return ValueTask.CompletedTask;
            }
        );

        await using (var child = factory.Begin(_Services))
        {
            await child.SignalAsync(CommitOutcome.RolledBack);
        }

        root.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);

        // Root already terminal: this commit signal is ignored and logged.
        await root.SignalAsync(CommitOutcome.Committed);

        rootCalls.Should().Be(0);
        root.Coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);

        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("ignoring");
        warning.Message.Should().Contain("Committed");
    }

    [Fact]
    public async Task should_coexist_durable_and_in_memory_buffers_when_relational_capability_present()
    {
        var capability = new StubRelationalCommitContext();
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);

        await using var scope = factory.BeginNew(_Services, [capability]);
        var coordinator = scope.Coordinator;

        var inMemory = coordinator.GetOrAdd(_ => new InMemoryWorkBuffer<string>());
        var durable = coordinator.GetOrAdd(c => new RecordingDurableWorkBuffer(c));

        inMemory.Add("queued");

        // Durable enlist must not throw while the relational capability is present.
        await durable.EnlistAsync("row-1", CancellationToken.None);

        inMemory.Drain().Should().Equal(["queued"]);
        durable.WrittenRows.Should().Equal(["row-1"]);

        await scope.SignalAsync(CommitOutcome.Committed);
    }

    [Fact]
    public async Task should_throw_at_enlist_when_durable_work_lacks_relational_capability_and_policy_is_throw()
    {
        var coordinator = new CommitCoordinator();
        var durable = coordinator.GetOrAdd(c => new RecordingDurableWorkBuffer(
            c,
            DurableWorkProviderMismatchPolicy.Throw
        ));

        var act = () => durable.EnlistAsync("row-1", CancellationToken.None).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Durable commit work requires IRelationalCommitContext.");
    }

    [Fact]
    public async Task should_tolerate_missing_relational_capability_when_durable_policy_is_warn()
    {
        var coordinator = new CommitCoordinator();
        var durable = coordinator.GetOrAdd(c => new RecordingDurableWorkBuffer(
            c,
            DurableWorkProviderMismatchPolicy.Warn
        ));

        await durable.EnlistAsync("row-1", CancellationToken.None);

        durable.FallbackRows.Should().Equal(["row-1"]);
        durable.WrittenRows.Should().BeEmpty();
    }

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

                await using var scope = factory.Begin(_Services);

                scope.Coordinator.OnCommit(
                    async (_, ct) =>
                    {
                        // Force a real continuation back onto the captured context.
                        await Task.Yield();
                        await Task.Delay(1, ct);
                    }
                );

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

    private sealed class StubRelationalCommitContext : IRelationalCommitContext
    {
        public System.Data.Common.DbConnection? Connection => null;

        public System.Data.Common.DbTransaction? Transaction => null;
    }

    private sealed class RecordingDurableWorkBuffer(
        ICommitCoordinator coordinator,
        DurableWorkProviderMismatchPolicy policy = DurableWorkProviderMismatchPolicy.Throw
    ) : DurableWorkBuffer<string>(coordinator, policy)
    {
        public List<string> WrittenRows { get; } = [];

        public List<string> FallbackRows { get; } = [];

        protected override ValueTask WriteRowAsync(
            string row,
            IRelationalCommitContext relationalContext,
            CancellationToken cancellationToken
        )
        {
            WrittenRows.Add(row);

            return ValueTask.CompletedTask;
        }

        protected override ValueTask EnlistWithoutRelationalContextAsync(
            string row,
            CancellationToken cancellationToken
        )
        {
            FallbackRows.Add(row);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

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
