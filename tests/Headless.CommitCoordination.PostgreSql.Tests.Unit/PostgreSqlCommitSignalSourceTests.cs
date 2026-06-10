// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class PostgreSqlCommitSignalSourceTests
{
    [Fact]
    public async Task should_signal_without_disposing_the_callers_ambient_scope()
    {
        var stack = new CommitScopeStack();
        var source = new PostgreSqlCommitSignalSource(
            new CommitScopeFactory(stack),
            NullLogger<PostgreSqlCommitSignalSource>.Instance
        );
        var calls = 0;
        var key = new object();
        var scope = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new EmptyServiceProvider(),
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

            stack.Current.Should().BeSameAs(scope.Coordinator);
        }

        calls.Should().Be(1);
        stack.Current.Should().BeNull();
    }

    [Fact]
    public void should_throw_when_provider_key_already_has_active_scope()
    {
        var source = new PostgreSqlCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<PostgreSqlCommitSignalSource>.Instance
        );
        var key = new object();
        using var first = source.Attach(
            new CommitCoordinatorBindings
            {
                Services = new EmptyServiceProvider(),
                ProviderTransactionKey = key,
            },
            CancellationToken.None
        );

        source
            .Invoking(x => x.Attach(
                new CommitCoordinatorBindings
                {
                    Services = new EmptyServiceProvider(),
                    ProviderTransactionKey = key,
                },
                CancellationToken.None
            ))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("A PostgreSQL commit coordination scope is already attached for this provider transaction key.");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
