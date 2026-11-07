// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Tests;

public abstract partial class DataStorageTestsBase
{
    // Fingerprints in the shape the shared algorithm returns; storage stores what the caller computed, never derives one.
    private const string _FirstFingerprint = "v1:0123456789abcdef";
    private const string _SecondFingerprint = "v1:fedcba9876543210";

    /// <summary>A row's stored failure record, read below the storage.</summary>
    protected readonly record struct PersistedFailure(
        string? ConsumerIdentity,
        string? ExceptionInfo,
        string? FailureFingerprint
    );

    /// <summary>Reads a row's stored failure text and fingerprint directly from the store.</summary>
    protected virtual Task<PersistedFailure?> ReadPersistedFailureAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("This storage cannot read a stored failure record.");
    }

    /// <summary>Reads the stored failure record of every received row of <paramref name="messageId"/>.</summary>
    protected virtual Task<IReadOnlyList<PersistedFailure>> ReadReceivedFailuresAsync(
        string messageId,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("This storage cannot read stored failure records.");
    }

    /// <summary>Overwrites a row's stored envelope with text no claim can deserialize.</summary>
    protected virtual Task CorruptStoredContentAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException("This storage does not deserialize stored envelopes in its claim.");
    }

    public virtual async Task should_store_the_failure_text_and_fingerprint_in_the_terminal_failure_write(
        bool published
    )
    {
        // given
        var storage = GetStorage();
        var message = await _StoreForFailureAsync(storage, published, "fingerprint-terminal");

        // when: the terminal failure write carries the failure
        message.ExceptionInfo = "InvalidOperationException: order 1001 is out of stock";
        message.FailureFingerprint = _FirstFingerprint;
        (await _WriteStateAsync(storage, published, message, StatusName.Failed, retryDelay: null)).Should().BeTrue();

        // then: the row holds both, and the monitoring read returns them
        var stored = await ReadPersistedFailureAsync(published, message.StorageId, AbortToken);
        stored.Should().NotBeNull();
        stored!.Value.ExceptionInfo.Should().Be("InvalidOperationException: order 1001 is out of stock");
        stored.Value.FailureFingerprint.Should().Be(_FirstFingerprint);

        var monitoring = storage.GetMonitoringApi();
        var read = published
            ? await monitoring.GetPublishedMessageAsync(message.StorageId, AbortToken)
            : await monitoring.GetReceivedMessageAsync(message.StorageId, AbortToken);
        read.Should().NotBeNull();
        read!.ExceptionInfo.Should().Be("InvalidOperationException: order 1001 is out of stock");
        read.FailureFingerprint.Should().Be(_FirstFingerprint);
    }

    public virtual async Task should_keep_the_first_failure_when_a_later_failure_write_meets_the_terminal_row(
        bool published
    )
    {
        // given: a row that already ended in a failure
        var storage = GetStorage();
        var message = await _StoreForFailureAsync(storage, published, "fingerprint-guard");
        message.ExceptionInfo = "first failure";
        message.FailureFingerprint = _FirstFingerprint;
        (await _WriteStateAsync(storage, published, message, StatusName.Failed, retryDelay: null)).Should().BeTrue();

        // when: a redelivery's failure write reaches the terminal row
        message.ExceptionInfo = "second failure";
        message.FailureFingerprint = _SecondFingerprint;
        var applied = await _WriteStateAsync(storage, published, message, StatusName.Failed, retryDelay: null);

        // then: the guard wins and the row keeps the failure that ended it
        applied.Should().BeFalse();
        var stored = await ReadPersistedFailureAsync(published, message.StorageId, AbortToken);
        stored!.Value.ExceptionInfo.Should().Be("first failure");
        stored.Value.FailureFingerprint.Should().Be(_FirstFingerprint);
    }

    public virtual async Task should_store_the_failure_on_a_retry_write_and_accept_the_success_that_follows(
        bool published
    )
    {
        // given: a failure that leaves a retry pending
        var storage = GetStorage();
        var message = await _StoreForFailureAsync(storage, published, "fingerprint-retry");
        message.Retries = 1;
        message.ExceptionInfo = "TimeoutException: the broker did not answer";
        message.FailureFingerprint = _FirstFingerprint;
        (await _WriteStateAsync(storage, published, message, StatusName.Failed, TimeSpan.FromMinutes(5)))
            .Should()
            .BeTrue();
        var retried = await ReadPersistedFailureAsync(published, message.StorageId, AbortToken);

        // when: the retry succeeds, and a late failure write arrives after it
        var succeeded = await _WriteStateAsync(storage, published, message, StatusName.Succeeded, retryDelay: null);
        message.ExceptionInfo = "late failure";
        message.FailureFingerprint = _SecondFingerprint;
        var late = await _WriteStateAsync(storage, published, message, StatusName.Failed, retryDelay: null);

        // then: the retry write stored the failure, the success landed, and the terminal guard still refuses the late
        // write
        retried!.Value.ExceptionInfo.Should().Be("TimeoutException: the broker did not answer");
        retried.Value.FailureFingerprint.Should().Be(_FirstFingerprint);
        succeeded.Should().BeTrue();
        late.Should().BeFalse();
        var monitoring = storage.GetMonitoringApi();
        var read = published
            ? await monitoring.GetPublishedMessageAsync(message.StorageId, AbortToken)
            : await monitoring.GetReceivedMessageAsync(message.StorageId, AbortToken);
        read!.StatusName.Should().Be(StatusName.Succeeded);
        (await ReadPersistedFailureAsync(published, message.StorageId, AbortToken))!
            .Value.FailureFingerprint.Should()
            .NotBe(_SecondFingerprint);
    }

    public virtual async Task should_store_the_poison_cause_for_a_failure_stored_on_arrival_without_one()
    {
        // given
        var storage = GetStorage();
        var messageId = $"fingerprint-arrival-{Guid.NewGuid():N}";

        // when: a delivery rejected on arrival is stored from its raw content, with no fingerprint to give
        (
            await storage.StoreReceivedExceptionMessageAsync(
                "fingerprint-arrival",
                "fingerprint-arrival-group",
                GetSerializer().Serialize(CreateMessage(messageId)),
                "SubscriberNotFoundException: no consumer",
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        // then
        var stored = (await ReadReceivedFailuresAsync(messageId, AbortToken)).Should().ContainSingle().Subject;
        stored.ExceptionInfo.Should().Be("SubscriberNotFoundException: no consumer");
        stored.FailureFingerprint.Should().Be("v1:poison");
    }

    public virtual async Task should_store_the_cause_a_failure_stored_on_arrival_names()
    {
        // given
        var storage = GetStorage();
        var messageId = $"fingerprint-arrival-named-{Guid.NewGuid():N}";
        var origin = CreateMessage(messageId);

        // when: the receive stage names the deserialization cause
        (
            await storage.StoreReceivedExceptionMessageAsync(
                "fingerprint-arrival",
                "fingerprint-arrival-group",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = GetSerializer().Serialize(origin),
                    Lane = MessageLane.Bus,
                    FailureFingerprint = "v1:deserialization",
                },
                "MessageDeserializationException: bad payload",
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        // then
        var stored = (await ReadReceivedFailuresAsync(messageId, AbortToken)).Should().ContainSingle().Subject;
        stored.FailureFingerprint.Should().Be("v1:deserialization");
    }

    public virtual async Task should_keep_the_failures_of_consumer_identities_that_differ_by_a_trailing_space_apart()
    {
        // given: one message rejected on arrival for two consumers whose identities differ only by a trailing space
        var storage = GetStorage();
        var messageId = $"fingerprint-padded-{Guid.NewGuid():N}";
        var origin = CreateMessage(messageId);

        async Task<bool> store(string consumerIdentity, string fingerprint) =>
            await storage.StoreReceivedExceptionMessageAsync(
                "fingerprint-padded",
                consumerIdentity,
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = GetSerializer().Serialize(origin),
                    Lane = MessageLane.Bus,
                    FailureFingerprint = fingerprint,
                },
                "rejected",
                AbortToken
            );

        // when
        (await store("fingerprint-group", "v1:poison"))
            .Should()
            .BeTrue();
        (await store("fingerprint-group ", "v1:deserialization")).Should().BeTrue();

        // then: two rows, whose stored identities still differ byte for byte, each with its own failure
        var stored = await ReadReceivedFailuresAsync(messageId, AbortToken);
        stored.Should().HaveCount(2);
        stored
            .Select(failure => (failure.ConsumerIdentity, failure.FailureFingerprint))
            .Should()
            .BeEquivalentTo([("fingerprint-group", "v1:poison"), ("fingerprint-group ", "v1:deserialization")]);
    }

    public virtual async Task should_store_the_poison_cause_when_a_claim_cannot_read_a_stored_row(bool published)
    {
        // given: a row due for a retry whose stored envelope no longer deserializes
        var storage = GetStorage();
        var message = await _StoreRetryDueAsync(storage, published, "fingerprint-poison-claim");
        await CorruptStoredContentAsync(published, message.StorageId, AbortToken);

        // when: the retry pickup claims it
        var claimed = await _PickUpRetriesAsync(storage, published);

        // then: the claim skips it and ends it with its own failure text and the fixed poison cause
        claimed.Should().NotContain(m => m.StorageId == message.StorageId);
        var stored = await ReadPersistedFailureAsync(published, message.StorageId, AbortToken);
        stored.Should().NotBeNull();
        stored!.Value.ExceptionInfo.Should().NotBeNullOrWhiteSpace();
        stored.Value.FailureFingerprint.Should().Be("v1:poison");
    }

    private static async Task<MediumMessage> _StoreForFailureAsync(IDataStorage storage, bool published, string name)
    {
        return published
            ? await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(name, $"{name}-group", CreateMessage(), AbortToken);
    }

    private static async Task<bool> _WriteStateAsync(
        IDataStorage storage,
        bool published,
        MediumMessage message,
        StatusName state,
        TimeSpan? retryDelay
    )
    {
        var delay = retryDelay is { } value ? RetryDelay.Exactly(value) : (RetryDelay?)null;

        return published
            ? await storage.ChangePublishStateAsync(message, state, retryDelay: delay, cancellationToken: AbortToken)
            : await storage.ChangeReceiveStateAsync(message, state, retryDelay: delay, cancellationToken: AbortToken);
    }
}
