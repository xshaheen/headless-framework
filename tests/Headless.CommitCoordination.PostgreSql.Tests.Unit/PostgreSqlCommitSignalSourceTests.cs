// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
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
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var scope = source.Attach(
            new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
            CancellationToken.None
        );

        await using (scope)
        {
            scope.Coordinator.OnCommit(
                (_, _) =>
                {
                    calls++;

                    return ValueTask.CompletedTask;
                }
            );

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
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var first = source.Attach(
            new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
            CancellationToken.None
        );

        source
            .Invoking(x =>
                x.Attach(
                    new CommitCoordinatorBindings { Services = provider, ProviderTransactionKey = key },
                    CancellationToken.None
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "A PostgreSQL commit coordination scope is already attached for this provider transaction key."
            );
    }

    [Fact]
    public async Task should_keep_owned_service_scope_alive_until_drain_resolves_scoped_services()
    {
        var source = new PostgreSqlCommitSignalSource(
            new CommitScopeFactory(new CommitScopeStack()),
            NullLogger<PostgreSqlCommitSignalSource>.Instance
        );
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        await using var provider = services.BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();
        var key = new object();
        ScopedMarker? marker = null;

        var scope = source.Attach(
            new CommitCoordinatorBindings { Services = callerScope.ServiceProvider, ProviderTransactionKey = key },
            CancellationToken.None
        );

        scope.Coordinator.OnCommit(
            (context, _) =>
            {
                // The drain resolves from the source's OWNED child scope, not the caller's — so disposing the caller's
                // scope first must not strand the drain (mirrors the SqlServer owned-scope guarantee).
                marker = context.Services.GetRequiredService<ScopedMarker>();

                return ValueTask.CompletedTask;
            }
        );

        await callerScope.DisposeAsync();
        await source.SignalCommittedAsync(key, CancellationToken.None);
        await scope.DisposeAsync();

        marker.Should().NotBeNull();
    }

    private sealed class ScopedMarker;
}
