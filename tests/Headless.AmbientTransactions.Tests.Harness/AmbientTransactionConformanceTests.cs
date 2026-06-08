// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.Testing.Tests;

namespace Tests;

public abstract class AmbientTransactionConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : AmbientTransactionFixtureBase
{
    protected TFixture Fixture { get; } = fixture;

    public virtual async Task should_flush_registered_work_once_after_commit_async()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);
        var workKey = Faker.Random.AlphaNumeric(12);
        var drainCount = 0;

        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        await transaction.CommitAsync(AbortToken);
        transaction.CompleteExternally();

        drainCount.Should().Be(1);
        await Fixture.AssertCommittedWorkVisibleAsync(workKey, AbortToken);
    }

    public virtual async Task should_flush_registered_work_once_after_commit()
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
        transaction.CompleteExternally();

        drainCount.Should().Be(1);
    }

    public virtual async Task should_discard_registered_work_after_rollback_async()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);
        var workKey = Faker.Random.AlphaNumeric(12);
        var drainCount = 0;

        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        await transaction.RollbackAsync(AbortToken);
        transaction.CompleteExternally();

        drainCount.Should().Be(0);
        await Fixture.AssertRolledBackWorkAbsentAsync(workKey, AbortToken);
    }

    public virtual async Task should_discard_registered_work_after_rollback()
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

        transaction.Rollback();
        transaction.CompleteExternally();

        drainCount.Should().Be(0);
    }

    public virtual async Task should_complete_detached_external_transaction_without_retouching_current()
    {
        await Fixture.ResetAsync(AbortToken);
        var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );
        transaction.DbTransaction.Should().NotBeNull();
        transaction.DbTransaction = null;
        Fixture.CurrentAmbientTransaction.Current.Should().BeNull();

        await Fixture.CompleteExternalCommitAsync(transaction, AbortToken);
        await transaction.DisposeAsync();

        drainCount.Should().Be(1);
        Fixture.CurrentAmbientTransaction.Current.Should().BeNull();
    }

    public virtual async Task should_clear_current_when_transaction_detaches()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);

        transaction.DbTransaction.Should().NotBeNull();
        Fixture.CurrentAmbientTransaction.Current.Should().BeSameAs(transaction);

        transaction.DbTransaction = null;

        Fixture.CurrentAmbientTransaction.Current.Should().BeNull();
    }

    public virtual async Task should_set_auto_commit_flag_when_transaction_begins()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(autoCommit: true, cancellationToken: AbortToken);

        transaction.AutoCommit.Should().BeTrue();
        Fixture.CurrentAmbientTransaction.Current.Should().BeSameAs(transaction);
    }

    public virtual async Task should_honor_requested_isolation_level_when_supported()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken: AbortToken
        );

        transaction.DbTransaction.Should().NotBeNull();
    }

    public virtual async Task should_reject_registering_work_after_completion()
    {
        await Fixture.ResetAsync(AbortToken);
        await using var transaction = await Fixture.BeginTransactionAsync(cancellationToken: AbortToken);

        await transaction.CommitAsync(AbortToken);

        var act = () => transaction.RegisterCommitWork(_ => ValueTask.CompletedTask);

        act.Should().Throw<InvalidOperationException>();
    }
}
