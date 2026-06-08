// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.AmbientTransactions.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class InMemoryAmbientTransactionTests
    : AmbientTransactionConformanceTests<InMemoryAmbientTransactionFixture>
{
    public InMemoryAmbientTransactionTests()
        : base(new InMemoryAmbientTransactionFixture()) { }

    [Fact]
    public override Task should_flush_registered_work_once_after_commit_async()
    {
        return base.should_flush_registered_work_once_after_commit_async();
    }

    [Fact]
    public override Task should_flush_registered_work_once_after_commit()
    {
        return base.should_flush_registered_work_once_after_commit();
    }

    [Fact]
    public override Task should_discard_registered_work_after_rollback_async()
    {
        return base.should_discard_registered_work_after_rollback_async();
    }

    [Fact]
    public override Task should_discard_registered_work_after_rollback()
    {
        return base.should_discard_registered_work_after_rollback();
    }

    [Fact]
    public override Task should_complete_detached_external_transaction_without_retouching_current()
    {
        return base.should_complete_detached_external_transaction_without_retouching_current();
    }

    [Fact]
    public override Task should_clear_current_when_transaction_detaches()
    {
        return base.should_clear_current_when_transaction_detaches();
    }

    [Fact]
    public override Task should_set_auto_commit_flag_when_transaction_begins()
    {
        return base.should_set_auto_commit_flag_when_transaction_begins();
    }

    [Fact]
    public override Task should_honor_requested_isolation_level_when_supported()
    {
        return base.should_honor_requested_isolation_level_when_supported();
    }

    [Fact]
    public override Task should_reject_registering_work_after_completion()
    {
        return base.should_reject_registering_work_after_completion();
    }

    [Fact]
    public async Task setup_should_register_current_accessor_and_transaction()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddInMemoryAmbientTransactions();
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentAmbientTransaction>().Should().BeOfType<AsyncLocalCurrentAmbientTransaction>();
        provider.GetRequiredService<IAmbientTransaction>().Should().BeOfType<InMemoryAmbientTransaction>();
    }

    [Fact]
    public async Task concurrent_transactions_should_keep_buffers_isolated()
    {
        // given
        var current = new AsyncLocalCurrentAmbientTransaction();
        await using var first = new InMemoryAmbientTransaction(current);
        await using var second = new InMemoryAmbientTransaction(current);
        first.Begin();
        second.Begin();
        var firstDrainCount = 0;
        var secondDrainCount = 0;
        first.RegisterCommitWork(
            _ =>
            {
                firstDrainCount++;
                return ValueTask.CompletedTask;
            }
        );
        second.RegisterCommitWork(
            _ =>
            {
                secondDrainCount++;
                return ValueTask.CompletedTask;
            }
        );

        // when
        await first.CommitAsync();

        // then
        firstDrainCount.Should().Be(1);
        secondDrainCount.Should().Be(0);
    }
}

public sealed class InMemoryAmbientTransactionFixture : AmbientTransactionFixtureBase
{
    private readonly AsyncLocalCurrentAmbientTransaction _current = new();

    public override ICurrentAmbientTransaction CurrentAmbientTransaction => _current;

    public override ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        _current.Current = null;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<IAmbientTransaction> BeginTransactionAsync(
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        var transaction = new InMemoryAmbientTransaction(_current);
        transaction.Begin(autoCommit);

        return ValueTask.FromResult<IAmbientTransaction>(transaction);
    }
}
