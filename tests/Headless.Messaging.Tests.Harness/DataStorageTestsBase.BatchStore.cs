// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

/// <summary>
/// How a relational storage writes a run of published rows of one transaction together: every row lands, in the order
/// given, with the database's times, and the run shares the transaction's fate.
/// </summary>
public abstract partial class DataStorageTestsBase
{
    /// <summary>
    /// Opens a connection to the storage's database for a test that writes inside its own transaction, or returns
    /// <see langword="null" /> when the storage has none.
    /// </summary>
    protected virtual Task<DbConnection?> OpenDatabaseConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DbConnection?>(null);

    [Fact]
    public async Task should_store_every_batched_published_row_when_the_run_spans_several_batches()
    {
        // given — enough rows that the run needs more than one batch under the per-batch parameter cap, and one
        // scheduled row in the middle
        var (store, connection) = await _BatchStoreAsync();
        await using var _ = connection;
        var publishAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var rows = Enumerable
            .Range(0, 301)
            .Select(i =>
                (Name: "batched-store", Message: store.CreatePublishedMessage(_Envelope(), i == 150 ? publishAt : null))
            )
            .ToList();

        // when
        await using (var transaction = await connection.BeginTransactionAsync(AbortToken))
        {
            await store.StorePublishedMessagesAsync(rows, transaction, AbortToken);
            await transaction.CommitAsync(AbortToken);
        }

        // then
        rows.Select(row => row.Message.StorageId).Should().OnlyHaveUniqueItems();
        foreach (var (_, message) in rows)
        {
            message.Added.Should().NotBe(default);
            var stored = await GetStorage().GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken);
            stored.Should().NotBeNull();
            stored!.Origin.Id.Should().Be(message.Origin.Id);
        }

        rows[150].Message.NextRetryAt.Should().BeNull();
        rows[0].Message.NextRetryAt.Should().NotBeNull();
        var scheduled = await GetStorage()
            .GetMonitoringApi()
            .GetPublishedMessageAsync(rows[150].Message.StorageId, AbortToken);
        scheduled!.ExpiresAt.Should().BeCloseTo(publishAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task should_store_no_batched_published_row_when_the_transaction_rolls_back()
    {
        // given
        var (store, connection) = await _BatchStoreAsync();
        await using var _ = connection;
        var rows = Enumerable
            .Range(0, 3)
            .Select(_ => (Name: "batched-rollback", Message: store.CreatePublishedMessage(_Envelope(), null)))
            .ToList();

        // when
        await using (var transaction = await connection.BeginTransactionAsync(AbortToken))
        {
            await store.StorePublishedMessagesAsync(rows, transaction, AbortToken);
            await transaction.RollbackAsync(AbortToken);
        }

        // then
        foreach (var (_, message) in rows)
        {
            (await GetStorage().GetMonitoringApi().GetPublishedMessageAsync(message.StorageId, AbortToken))
                .Should()
                .BeNull();
        }
    }

    [Fact]
    public async Task should_throw_the_driver_error_when_a_batched_row_conflicts()
    {
        // given — a later row reuses an earlier row's identifier, the failure a single store raises as well
        var (store, connection) = await _BatchStoreAsync();
        await using var _ = connection;
        var rows = Enumerable
            .Range(0, 5)
            .Select(_ => (Name: "batched-conflict", Message: store.CreatePublishedMessage(_Envelope(), null)))
            .ToList();
        rows[3].Message.StorageId = rows[1].Message.StorageId;
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);

        // when
        var act = async () => await store.StorePublishedMessagesAsync(rows, transaction, AbortToken);

        // then
        await act.Should().ThrowAsync<DbException>();
    }

    private async Task<(IBatchPublishedMessageStore Store, DbConnection Connection)> _BatchStoreAsync()
    {
        if (GetStorage() is not IBatchPublishedMessageStore store)
        {
            Assert.Skip("This storage does not batch published rows.");
            throw new UnreachableException();
        }

        var connection = await OpenDatabaseConnectionAsync(AbortToken);
        if (connection is null)
        {
            Assert.Skip("This storage has no database connection a test can open a transaction on.");
            throw new UnreachableException();
        }

        return (store, connection);
    }

    private static MediumMessage _Envelope()
    {
        return new MediumMessage
        {
            StorageId = Guid.Empty,
            Origin = CreateMessage(),
            Content = string.Empty,
            Lane = MessageLane.Bus,
        };
    }
}
