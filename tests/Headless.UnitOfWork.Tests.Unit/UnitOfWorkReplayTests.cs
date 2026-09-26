// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Net.Sockets;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// The per-attempt-connection runner behind <c>RunAsync(NpgsqlDataSource, …)</c> and
/// <c>RunAsync(Func&lt;CancellationToken, ValueTask&lt;SqlConnection&gt;&gt;, …)</c>: which replay policy applies, and
/// that each attempt owns, and outlives, nothing but its own connection. The providers' real behavior against a
/// database is pinned by the replay conformance suite.
/// </summary>
public sealed class UnitOfWorkReplayTests : TestBase
{
    private static RetryStrategyOptions _ReplayEverythingOnce() =>
        new()
        {
            MaxRetryAttempts = 1,
            Delay = TimeSpan.Zero,
            ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is not null),
        };

    [Fact]
    public async Task should_not_replay_when_neither_the_host_nor_the_call_configures_a_strategy()
    {
        var run = new Run(new UnitOfWorkFactory());

        var act = () => run.ExecuteAsync(failFirstAttempts: 1, retry: null, AbortToken);

        await act.Should().ThrowAsync<TransientFaultException>();
        run.Attempts.Should().Be(1, "replay is off by default");
    }

    [Fact]
    public async Task should_replay_with_the_host_default_when_the_call_passes_none()
    {
        var run = new Run(
            new UnitOfWorkFactory(retryOptions: new UnitOfWorkRetryOptions { RetryStrategy = _ReplayEverythingOnce() })
        );

        var result = await run.ExecuteAsync(failFirstAttempts: 1, retry: null, AbortToken);

        result.Should().Be(2);
        run.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_prefer_the_calls_strategy_over_the_host_default()
    {
        var run = new Run(
            new UnitOfWorkFactory(retryOptions: new UnitOfWorkRetryOptions { RetryStrategy = _ReplayEverythingOnce() })
        );
        var never = new RetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            Delay = TimeSpan.Zero,
            ShouldHandle = static _ => ValueTask.FromResult(false),
        };

        var act = () => run.ExecuteAsync(failFirstAttempts: 1, retry: never, AbortToken);

        await act.Should().ThrowAsync<TransientFaultException>();
        run.Attempts.Should().Be(1, "the call's own strategy classified the fault as permanent");
    }

    [Fact]
    public async Task should_replay_with_the_calls_strategy_on_a_factory_it_does_not_own()
    {
        // A foreign IUnitOfWorkFactory has no host default to fall back on, but a per-call strategy still applies.
        var foreign = Substitute.For<IUnitOfWorkFactory>();
        var owned = new UnitOfWorkFactory();
        foreign
            .BeginAsync(
                Arg.Any<Func<CancellationToken, ValueTask<IUnitOfWorkResource>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                owned.BeginAsync(call.Arg<Func<CancellationToken, ValueTask<IUnitOfWorkResource>>>(), AbortToken)
            );
        var run = new Run(foreign);

        var result = await run.ExecuteAsync(failFirstAttempts: 1, retry: _ReplayEverythingOnce(), AbortToken);

        result.Should().Be(2);
        run.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_run_each_attempt_on_its_own_connection_and_dispose_it_after_the_unit_is_unwound()
    {
        var run = new Run(new UnitOfWorkFactory());

        await run.ExecuteAsync(failFirstAttempts: 1, retry: _ReplayEverythingOnce(), AbortToken);

        run.Connections.Should().HaveCount(2, "a replay never reuses the connection a failed attempt used");
        run.Connections.Should().OnlyContain(connection => connection.Disposed);
        run.Connections[0]
            .ResourceRollbackCallsWhenDisposed.Should()
            .Be(1, "the failed attempt's transaction is rolled back before its connection goes away");
        run.Connections[1].ResourceCommitCallsWhenDisposed.Should().Be(1);
    }

    [Fact]
    public async Task should_dispose_the_connection_when_the_attempt_is_not_replayed()
    {
        var run = new Run(new UnitOfWorkFactory());

        var act = () => run.ExecuteAsync(failFirstAttempts: 1, retry: null, AbortToken);

        await act.Should().ThrowAsync<TransientFaultException>();
        run.Connections.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_replay_a_commit_fault_even_when_the_strategy_classifies_it_as_transient()
    {
        var run = new Run(new UnitOfWorkFactory()) { CommitFault = new TransientFaultException() };

        var act = () => run.ExecuteAsync(failFirstAttempts: 0, retry: _ReplayEverythingOnce(), AbortToken);

        await act.Should().ThrowAsync<TransientFaultException>();
        run.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_reject_an_invalid_strategy_with_pollys_validation()
    {
        var run = new Run(new UnitOfWorkFactory());
        var invalid = new RetryStrategyOptions { MaxRetryAttempts = -1 };

        var act = () => run.ExecuteAsync(failFirstAttempts: 0, retry: invalid, AbortToken);

        await act.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>();
        run.Attempts.Should().Be(0);
    }

    [Fact]
    public void should_resolve_the_host_default_from_configured_options()
    {
        var retry = _ReplayEverythingOnce();
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        services.Configure<UnitOfWorkRetryOptions>(options => options.RetryStrategy = retry);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<UnitOfWorkRetryOptions>>().Value.RetryStrategy.Should().BeSameAs(retry);
        ((UnitOfWorkFactory)provider.GetRequiredService<IUnitOfWorkFactory>())
            .DefaultReplayStrategy.Should()
            .BeOfType<ResiliencePipelineUnitOfWorkExecutionStrategy>();
    }

    [Fact]
    public void should_default_to_no_replay()
    {
        new UnitOfWorkRetryOptions().RetryStrategy.Should().BeNull();
    }

    [Fact]
    public async Task should_handle_a_transient_relational_fault_by_default()
    {
        var transient = new FakeDbException(isTransient: true, inner: new SocketException());

        (await _DefaultShouldHandleAsync(transient, CancellationToken.None)).Should().BeTrue();
        (await _DefaultShouldHandleAsync(new FakeDbException(sqlState: "40001"), CancellationToken.None))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_not_handle_a_permanent_fault_or_a_cancellation_by_default()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        (await _DefaultShouldHandleAsync(new FakeDbException(sqlState: "23505"), CancellationToken.None))
            .Should()
            .BeFalse();
        (await _DefaultShouldHandleAsync(new InvalidOperationException(), CancellationToken.None)).Should().BeFalse();
        (await _DefaultShouldHandleAsync(new OperationCanceledException(), CancellationToken.None)).Should().BeFalse();
        (await _DefaultShouldHandleAsync(new FakeDbException(isTransient: true), cancelled.Token)).Should().BeFalse();
    }

    private static async ValueTask<bool> _DefaultShouldHandleAsync(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var context = ResilienceContextPool.Shared.Get(cancellationToken);

        try
        {
            return await UnitOfWorkRetryOptions.DefaultShouldHandle(
                new RetryPredicateArguments<object>(context, Outcome.FromException<object>(exception), attemptNumber: 0)
            );
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private sealed class TransientFaultException() : Exception("Simulated transient failure.");

    /// <summary>One RunPerAttemptConnectionAsync call over fake connections and fake owned resources.</summary>
    private sealed class Run(IUnitOfWorkFactory factory)
    {
        public List<FakeConnection> Connections { get; } = [];

        public int Attempts { get; private set; }

        public Exception? CommitFault { get; init; }

        public Task<int> ExecuteAsync(
            int failFirstAttempts,
            RetryStrategyOptions? retry,
            CancellationToken cancellationToken
        )
        {
            return UnitOfWorkRunner.RunPerAttemptConnectionAsync(
                factory,
                _ =>
                {
                    var connection = new FakeConnection(new FakeUnitOfWorkResource { CommitFault = CommitFault });
                    Connections.Add(connection);

                    return ValueTask.FromResult(connection);
                },
                (connection, ct) =>
                    factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(connection.Resource), ct),
                (_, _, _) =>
                {
                    Attempts++;

                    return Attempts <= failFirstAttempts
                        ? Task.FromException<int>(new TransientFaultException())
                        : Task.FromResult(Attempts);
                },
                retry,
                cancellationToken
            );
        }
    }

    private sealed class FakeConnection(FakeUnitOfWorkResource resource) : IAsyncDisposable
    {
        public FakeUnitOfWorkResource Resource { get; } = resource;

        public bool Disposed { get; private set; }

        public int ResourceRollbackCallsWhenDisposed { get; private set; }

        public int ResourceCommitCallsWhenDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            ResourceRollbackCallsWhenDisposed = Resource.RollbackCalls;
            ResourceCommitCallsWhenDisposed = Resource.CommitCalls;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDbException(string? sqlState = null, bool isTransient = false, Exception? inner = null)
        : DbException("fake database failure", inner)
    {
        public override string? SqlState { get; } = sqlState;

        public override bool IsTransient { get; } = isTransient;
    }
}
