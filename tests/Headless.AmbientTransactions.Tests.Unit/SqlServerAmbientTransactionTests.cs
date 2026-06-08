// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.AmbientTransactions.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SqlServerAmbientTransactionTests
    : AmbientTransactionConformanceTests<SqlServerAmbientTransactionFixture>
{
    public SqlServerAmbientTransactionTests()
        : base(new SqlServerAmbientTransactionFixture()) { }

    [Fact]
    public override async Task should_flush_registered_work_once_after_commit_async()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        await transaction.CommitAsync(AbortToken);
        drainCount.Should().Be(0);

        transaction.CompleteExternally();
        transaction.CompleteExternally();

        drainCount.Should().Be(1);
    }

    [Fact]
    public override async Task should_flush_registered_work_once_after_commit()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        transaction.Commit();
        drainCount.Should().Be(0);

        transaction.CompleteExternally();
        transaction.CompleteExternally();

        drainCount.Should().Be(1);
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
    public override async Task should_reject_registering_work_after_completion()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);

        await transaction.CommitAsync(AbortToken);
        transaction.CompleteExternally();

        var act = () => transaction.RegisterCommitWork(_ => ValueTask.CompletedTask);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task setup_should_register_current_accessor_and_transaction()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddSqlServerAmbientTransactions();
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentAmbientTransaction>().Should().BeOfType<AsyncLocalCurrentAmbientTransaction>();
        provider.GetRequiredService<IAmbientTransaction>().Should().BeOfType<SqlServerAmbientTransaction>();
    }
}

public sealed class SqlServerAmbientTransactionFixture : RecordingDbAmbientTransactionFixture
{
    protected override IAmbientTransaction CreateTransaction(ICurrentAmbientTransaction current)
    {
        return new SqlServerAmbientTransaction(current);
    }
}
