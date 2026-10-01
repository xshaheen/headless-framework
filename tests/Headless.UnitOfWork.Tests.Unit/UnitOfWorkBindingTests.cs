// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

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
            await RollbackGate.Task;
        }
    }
}
