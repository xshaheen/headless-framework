// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class SqlServerCommitSignalSourceTests
{
    [Fact]
    public async Task should_signal_attached_scope_by_provider_key()
    {
        var stack = new CommitScopeStack();
        var source = new SqlServerCommitSignalSource(
            new CommitScopeFactory(stack),
            NullLogger<SqlServerCommitSignalSource>.Instance
        );
        var calls = 0;
        var key = new object();
        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new ServiceCollection().BuildServiceProvider(),
                ProviderTransactionKey = key,
            },
            CancellationToken.None
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit((_, _) =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            await source.SignalCommittedAsync(key, CancellationToken.None);
        }

        calls.Should().Be(1);
        scope.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);
    }
}
