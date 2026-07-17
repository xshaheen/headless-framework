// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
namespace Tests;

public sealed class CommitCoordinationTransactionInterceptorTests : IDisposable
{
    // A real provider so Attach can CreateAsyncScope an owned drain scope (callbacks resolve scoped services that
    // outlive the request) — the same contract SqlServerCommitSignalSource imposes.
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public void Dispose()
    {
        _services.Dispose();
    }

    [Fact]
    public void should_not_deadlock_when_sync_commit_override_drains_under_a_synchronization_context()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var signalSource = new EntityFrameworkCommitSignalSource(
            factory,
            NullLogger<EntityFrameworkCommitSignalSource>.Instance
        );
        var transaction = new FakeDbTransaction();
        var ran = false;

        var scope = signalSource.Attach(
            new CommitCoordinatorBindings { Services = _services, ProviderTransactionKey = transaction },
            CancellationToken.None
        );

        scope.Coordinator.OnCommit(
            async (_, _) =>
            {
                // Posts the continuation back to the captured SynchronizationContext; the sync interceptor override
                // would deadlock here unless it offloads the drain off the committing thread.
                await Task.Yield();
                ran = true;
            }
        );

        var interceptor = new CommitCoordinationTransactionInterceptor(signalSource);

        var completed = SingleThreadSynchronizationContext.Run(
            () => interceptor.TransactionCommitted(transaction, null!),
            TimeSpan.FromSeconds(10)
        );

        completed
            .Should()
            .BeTrue(
                "the sync TransactionCommitted override must offload the drain off the captured SynchronizationContext"
            );
        SpinWait.SpinUntil(() => ran, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_provider_key_already_has_active_scope()
    {
        var stack = new CommitScopeStack();
        var factory = new CommitScopeFactory(stack);
        var signalSource = new EntityFrameworkCommitSignalSource(
            factory,
            NullLogger<EntityFrameworkCommitSignalSource>.Instance
        );
        var key = new object();
        using var first = signalSource.Attach(
            new CommitCoordinatorBindings { Services = _services, ProviderTransactionKey = key },
            CancellationToken.None
        );

        signalSource
            .Invoking(x =>
                x.Attach(
                    new CommitCoordinatorBindings { Services = _services, ProviderTransactionKey = key },
                    CancellationToken.None
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("An EF Core commit coordination scope is already attached for this provider transaction key.");
    }

    private sealed class FakeDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
