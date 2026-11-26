// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.Data.Sqlite;

namespace Tests;

/// <summary>
/// The resource → unit binding's eviction rules, and in particular that the asynchronous lookup a begin uses has
/// finished abandoning a stale unit (its rollback, and the connection close that rides on it) before it returns.
/// </summary>
public sealed class UnitOfWorkBindingTests : TestBase
{
    [Fact]
    public async Task should_hand_back_a_live_unit_and_evict_a_terminal_one()
    {
        var binding = new UnitOfWorkBinding<object>();
        var key = new object();
        var factory = new UnitOfWorkFactory();
        var unit = await factory.BeginAsync(
            _ => ValueTask.FromResult<IUnitOfWorkResource>(new FakeUnitOfWorkResource()),
            AbortToken
        );
        binding.Bind(key, unit);

        (await binding.TryGetAsync(key)).Should().BeSameAs(unit);
        binding.TryGet(key, out var found).Should().BeTrue();
        found.Should().BeSameAs(unit);

        await unit.CompleteAsync(AbortToken);

        (await binding.TryGetAsync(key)).Should().BeNull("a terminal unit is evicted");
        binding.TryGet(key, out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_reserve_the_connection_and_refuse_new_work_until_commit_finishes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var commitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = Substitute.For<IUnitOfWorkResource>();
        resource.IsOwned.Returns(true);
        resource.CommitAsync(AbortToken).Returns(new ValueTask(commitGate.Task));
        var driver = new DbConnectionUnitOfWorkDriver<SqliteConnection>();
        var factory = new UnitOfWorkFactory();
        await using var unit = await factory.BeginAsync(_ => ValueTask.FromResult(resource), AbortToken);
        DbConnectionUnitOfWorkBinding.Bind(connection, unit);
        UnitOfWorkState? callbackState = null;
        using var registration = unit.OnCompleted(() =>
        {
            callbackState = unit.State;

            return ValueTask.CompletedTask;
        });

        var complete = unit.CompleteAsync(AbortToken).AsTask();

        try
        {
            unit.State.Should().Be(UnitOfWorkState.Completing);
            unit.Failure.Should().BeNull();
            callbackState.Should().BeNull();

            var read = () => connection.UnitOfWork();
            var begin = () => driver.BeginAsync(factory, connection, IsolationLevel.Unspecified, AbortToken).AsTask();
            var run = async () =>
                await driver.RunAsync(
                    factory,
                    connection,
                    (_, _) => Task.FromResult(true),
                    IsolationLevel.Unspecified,
                    AbortToken
                );
            var register = () => unit.OnCompleted(static () => ValueTask.CompletedTask);
            var completeAgain = () => unit.CompleteAsync(AbortToken).AsTask();

            read.Should().Throw<InvalidOperationException>().WithMessage("*completing*");
            await begin.Should().ThrowAsync<InvalidOperationException>().WithMessage("*completing*");
            await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*completing*");
            register.Should().Throw<InvalidOperationException>().WithMessage("*Completing*");
            await completeAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already completing*");
            await unit.RollbackAsync();
            await resource.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            commitGate.SetResult();
            await complete;
        }

        unit.State.Should().Be(UnitOfWorkState.Completed);
        callbackState.Should().Be(UnitOfWorkState.Completed);
        connection.UnitOfWork().Should().BeNull();
    }

    [Fact]
    public async Task should_abandon_a_stale_owned_unit_before_the_async_lookup_returns()
    {
        var binding = new UnitOfWorkBinding<object>();
        var key = new object();
        var factory = new UnitOfWorkFactory();
        var resource = new GatedResource();
        var unit = await factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(resource), AbortToken);
        UnitOfWorkFailure? failure = null;
        unit.OnFailed(f =>
        {
            failure = f;

            return ValueTask.CompletedTask;
        });
        binding.Bind(key, unit);

        // The transaction ended behind the unit's back (disposed by hand, a pooled reset).
        resource.TransactionCompleted = true;

        var lookup = binding.TryGetAsync(key);

        lookup
            .IsCompleted.Should()
            .BeFalse("the lookup waits for the stale unit's rollback instead of leaving it to the background");
        resource.RollbackStarted.Task.IsCompleted.Should().BeTrue();

        resource.RollbackGate.SetResult();

        (await lookup).Should().BeNull("a stale unit is never handed back");
        unit.State.Should().Be(UnitOfWorkState.Failed);
        failure!.Reason.Should().Be(UnitOfWorkFailureReason.Abandoned);
        (await binding.TryGetAsync(key)).Should().BeNull("the stale entry is gone");
    }

    [Fact]
    public async Task should_still_evict_a_stale_owned_unit_through_the_synchronous_lookup()
    {
        var binding = new UnitOfWorkBinding<object>();
        var key = new object();
        var factory = new UnitOfWorkFactory();
        var resource = new FakeUnitOfWorkResource();
        var unit = await factory.BeginAsync(_ => ValueTask.FromResult<IUnitOfWorkResource>(resource), AbortToken);
        binding.Bind(key, unit);
        resource.TransactionCompleted = true;

        binding.TryGet(key, out _).Should().BeFalse();

        unit.State.Should().Be(UnitOfWorkState.Failed, "the synchronous lookup abandons the stale unit as before");
    }

    /// <summary>An owned resource whose rollback blocks until the test releases it.</summary>
    private sealed class GatedResource : IRelationalUnitOfWorkResource
    {
        public TaskCompletionSource RollbackGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RollbackStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TransactionCompleted { get; set; }

        public bool IsOwned => true;

        public bool IsTransactionCompleted => TransactionCompleted;

        public DbConnection Connection => null!;

        public DbTransaction Transaction => null!;

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public async ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            RollbackStarted.TrySetResult();
            await RollbackGate.Task.Bounded();
        }
    }
}
