// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

/// <summary>
/// The sequence lookup a sequential consumer makes before it runs a keyed received row: an earlier unfinished row of
/// the same consumer and key blocks it, in admission order, and a row stops blocking, and stops being blockable, in
/// the write that makes it terminal.
/// </summary>
public abstract partial class DataStorageTestsBase
{
    private static readonly string _SequenceKeyA = new('a', 64);
    private static readonly string _SequenceKeyB = new('b', 64);

    [Fact]
    public async Task should_never_block_a_received_row_admitted_without_a_sequence_key()
    {
        // given: an unfinished keyed row, then a row of the same consumer without a key
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        _ = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var unkeyed = await _AdmitSequencedAsync(storage, consumer, sequenceKey: null);

        // when
        var block = await storage.GetSequenceBlockAsync(unkeyed, AbortToken);

        // then
        block.Should().BeNull();
    }

    [Fact]
    public async Task should_block_a_keyed_row_while_an_earlier_row_of_its_key_is_unfinished()
    {
        // given
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        var earlier = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var later = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);

        // when
        var laterBlock = await storage.GetSequenceBlockAsync(later, AbortToken);
        var earlierBlock = await storage.GetSequenceBlockAsync(earlier, AbortToken);

        // then: only admission order blocks, so the earlier row is free to run
        laterBlock.Should().NotBeNull();
        earlierBlock.Should().BeNull();
    }

    [Fact]
    public async Task should_unblock_a_keyed_row_once_the_earlier_row_of_its_key_succeeds()
    {
        // given
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        var earlier = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var later = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);

        // when
        await _EndSequencedAttemptAsync(storage, earlier, StatusName.Succeeded, retryDelay: null);

        // then
        (await storage.GetSequenceBlockAsync(later, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_unblock_a_keyed_row_once_the_earlier_row_of_its_key_fails_for_good()
    {
        // given
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        var earlier = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var later = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);

        // when
        await _EndSequencedAttemptAsync(storage, earlier, StatusName.Failed, retryDelay: null);

        // then
        (await storage.GetSequenceBlockAsync(later, AbortToken))
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task should_keep_a_keyed_row_blocked_while_the_earlier_row_of_its_key_waits_for_a_delayed_retry()
    {
        // given
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        var earlier = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var later = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var retryDelay = TimeSpan.FromMinutes(5);

        // when
        var before = await _StorageNowAsync();
        await _EndSequencedAttemptAsync(storage, earlier, StatusName.Failed, RetryDelay.Exactly(retryDelay));
        var after = await _StorageNowAsync();
        var block = await storage.GetSequenceBlockAsync(later, AbortToken);

        // then: the block reports when the earlier row is due again, by the store's clock
        block.Should().NotBeNull();
        block!
            .Value.DueAt.Should()
            .NotBeNull()
            .And.BeOnOrAfter(before.Add(retryDelay))
            .And.BeOnOrBefore(after.Add(retryDelay));
    }

    [Fact]
    public async Task should_not_block_a_keyed_row_behind_another_consumer_or_another_key()
    {
        // given: an unfinished row of key A on one consumer
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        _ = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);

        // when: rows of the same key on another consumer, and of another key on the same consumer, arrive after it
        var otherConsumer = await _AdmitSequencedAsync(storage, _SequenceConsumer(), _SequenceKeyA);
        var otherKey = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyB);

        // then
        (await storage.GetSequenceBlockAsync(otherConsumer, AbortToken))
            .Should()
            .BeNull();
        (await storage.GetSequenceBlockAsync(otherKey, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_clear_the_sequence_key_of_a_row_in_the_write_that_makes_it_terminal()
    {
        // given: a later row blocked behind an earlier unfinished one
        var storage = GetStorage();
        var consumer = _SequenceConsumer();
        var earlier = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var later = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        (await storage.GetSequenceBlockAsync(later, AbortToken)).Should().NotBeNull();

        // when: the later row ends first, while the earlier one is still unfinished
        await _EndSequencedAttemptAsync(storage, later, StatusName.Succeeded, retryDelay: null);

        // then: the terminal row holds no key, so nothing can block it, and it blocks nothing after it
        (await storage.GetSequenceBlockAsync(later, AbortToken))
            .Should()
            .BeNull();
        var next = await _AdmitSequencedAsync(storage, consumer, _SequenceKeyA);
        var nextBlock = await storage.GetSequenceBlockAsync(next, AbortToken);
        nextBlock.Should().NotBeNull("the earlier row is still unfinished");
        await _EndSequencedAttemptAsync(storage, earlier, StatusName.Succeeded, retryDelay: null);
        (await storage.GetSequenceBlockAsync(next, AbortToken)).Should().BeNull();
    }

    private static string _SequenceConsumer() => $"orders.sequential-{Guid.NewGuid():N}";

    private static async Task<MediumMessage> _AdmitSequencedAsync(
        IDataStorage storage,
        string consumerIdentity,
        string? sequenceKey
    )
    {
        var origin = CreateMessage(messageName: "orders.sequenced");
        var admitted = await storage.AdmitReceivedMessageAsync(
            origin.Name,
            consumerIdentity,
            "v1",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = string.Empty,
                Lane = MessageLane.Bus,
                SequenceKey = sequenceKey,
            },
            cancellationToken: AbortToken
        );
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);

        return admitted.Message;
    }

    /// <summary>Runs one attempt of an admitted row and writes its outcome, the way a dispatch ends one.</summary>
    private static async Task _EndSequencedAttemptAsync(
        IDataStorage storage,
        MediumMessage message,
        StatusName state,
        RetryDelay? retryDelay
    )
    {
        var original = message.InlineAttempts;
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), original, AbortToken))
            .Should()
            .BeTrue();
        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                state,
                MessageContentWrite.Preserve,
                retryDelay,
                keepLease: false,
                message.Retries,
                message.InlineAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }
}
