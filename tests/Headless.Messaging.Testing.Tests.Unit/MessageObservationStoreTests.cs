// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Testing;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessageObservationStoreTests : TestBase
{
    private static RecordedMessage _MakeMessage(Type type, string id = "msg-1", object? payload = null) =>
        new()
        {
            MessageType = type,
            Message = payload ?? Activator.CreateInstance(type)!,
            MessageId = id,
            CorrelationId = null,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
            Topic = "test-topic",
            Timestamp = DateTimeOffset.UtcNow,
        };

    // --- Record ---

    [Fact]
    public void Record_adds_to_published_collection()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(SimpleMessage));

        // when
        store.Record(msg, MessageObservationType.Published);

        // then
        store.Published.Should().ContainSingle().Which.Should().Be(msg);
        store.Consumed.Should().BeEmpty();
        store.Faulted.Should().BeEmpty();
    }

    [Fact]
    public void Record_adds_to_consumed_collection()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(SimpleMessage));

        // when
        store.Record(msg, MessageObservationType.Consumed);

        // then
        store.Consumed.Should().ContainSingle().Which.Should().Be(msg);
        store.Published.Should().BeEmpty();
        store.Faulted.Should().BeEmpty();
    }

    [Fact]
    public void Record_adds_to_faulted_collection()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(SimpleMessage), payload: new SimpleMessage { Value = "err" });

        // when
        store.Record(msg, MessageObservationType.Faulted);

        // then
        store.Faulted.Should().ContainSingle().Which.Should().Be(msg);
        store.Published.Should().BeEmpty();
        store.Consumed.Should().BeEmpty();
    }

    [Fact]
    public void Record_multiple_messages_preserves_order()
    {
        // given
        var store = new MessageObservationStore();
        var m1 = _MakeMessage(typeof(SimpleMessage), "id-1");
        var m2 = _MakeMessage(typeof(SimpleMessage), "id-2");
        var m3 = _MakeMessage(typeof(SimpleMessage), "id-3");

        // when
        store.Record(m1, MessageObservationType.Published);
        store.Record(m2, MessageObservationType.Published);
        store.Record(m3, MessageObservationType.Published);

        // then
        store.Published.Should().HaveCount(3);
        store.Published.Select(m => m.MessageId).Should().ContainInOrder("id-1", "id-2", "id-3");
    }

    // --- WaitForAsync — already-existing messages ---

    [Fact]
    public async Task WaitForAsync_completes_immediately_when_message_already_exists()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(SimpleMessage));
        store.Record(msg, MessageObservationType.Published);

        // when
        var result = await store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Published,
            predicate: null,
            timeout: TimeSpan.FromMilliseconds(50),
            AbortToken
        );

        // then
        result.Should().Be(msg);
    }

    [Fact]
    public async Task WaitForAsync_matches_by_assignability()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(DerivedMessage));
        store.Record(msg, MessageObservationType.Consumed);

        // when — wait for the base type
        var result = await store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Consumed,
            predicate: null,
            timeout: TimeSpan.FromMilliseconds(50),
            AbortToken
        );

        // then
        result.Should().Be(msg);
    }

    // --- WaitForAsync — message arrives later ---

    [Fact]
    public async Task WaitForAsync_completes_when_message_arrives_after_registration()
    {
        // given
        var store = new MessageObservationStore();

        // when — start waiting before the message is recorded
        var waitTask = store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Consumed,
            predicate: null,
            timeout: TimeSpan.FromSeconds(5),
            AbortToken
        );

        // record from a background "thread"
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(30, AbortToken);
                var msg = _MakeMessage(typeof(SimpleMessage), "late-msg");
                store.Record(msg, MessageObservationType.Consumed);
            },
            AbortToken
        );

        var result = await waitTask;

        // then
        result.MessageId.Should().Be("late-msg");
    }

    // --- WaitForAsync — predicate ---

    [Fact]
    public async Task WaitForAsync_with_predicate_skips_non_matching_existing_messages()
    {
        // given
        var store = new MessageObservationStore();
        var wrongMsg = _MakeMessage(typeof(SimpleMessage), "wrong", new SimpleMessage { Value = "nope" });
        store.Record(wrongMsg, MessageObservationType.Published);

        // record the correct one after a short delay
        var correctMsg = _MakeMessage(typeof(SimpleMessage), "correct", new SimpleMessage { Value = "yes" });
        var waitTask = store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Published,
            predicate: p => p is SimpleMessage sm && sm.Value == "yes",
            timeout: TimeSpan.FromSeconds(5),
            AbortToken
        );

        _ = Task.Run(
            async () =>
            {
                await Task.Delay(30, AbortToken);
                store.Record(correctMsg, MessageObservationType.Published);
            },
            AbortToken
        );

        var result = await waitTask;

        // then
        result.Should().Be(correctMsg);
    }

    [Fact]
    public async Task WaitForAsync_with_predicate_matches_existing_when_predicate_true()
    {
        // given
        var store = new MessageObservationStore();
        var msg = _MakeMessage(typeof(SimpleMessage), "match", new SimpleMessage { Value = "match-me" });
        store.Record(msg, MessageObservationType.Consumed);

        // when
        var result = await store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Consumed,
            predicate: p => p is SimpleMessage sm && sm.Value == "match-me",
            timeout: TimeSpan.FromMilliseconds(50),
            AbortToken
        );

        // then
        result.Should().Be(msg);
    }

    // --- WaitForAsync — timeout ---

    [Fact]
    public async Task WaitForAsync_times_out_and_throws_MessageObservationTimeoutException()
    {
        // given
        var store = new MessageObservationStore();

        // when
        var act = async () =>
            await store.WaitForAsync(
                typeof(SimpleMessage),
                MessageObservationType.Published,
                predicate: null,
                timeout: TimeSpan.FromMilliseconds(50),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<MessageObservationTimeoutException>();
    }

    [Fact]
    public async Task WaitForAsync_timeout_exception_carries_diagnostic_info()
    {
        // given
        var store = new MessageObservationStore();

        // record a different message type so ObservedMessages is non-empty
        var unrelatedMsg = _MakeMessage(typeof(SimpleMessage), "unrelated");
        store.Record(unrelatedMsg, MessageObservationType.Published);

        // when — wait for a type that was never published
        MessageObservationTimeoutException? ex = null;

        try
        {
            await store.WaitForAsync(
                typeof(OtherMessage),
                MessageObservationType.Published,
                predicate: null,
                timeout: TimeSpan.FromMilliseconds(60),
                AbortToken
            );
        }
        catch (MessageObservationTimeoutException caught)
        {
            ex = caught;
        }

        // then
        ex.Should().NotBeNull();
        ex!.ExpectedType.Should().Be(typeof(OtherMessage));
        ex.ObservationType.Should().Be(MessageObservationType.Published);
        ex.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
        // ObservedMessages contains what was in the Published bucket
        ex.ObservedMessages.Should().ContainSingle().Which.MessageId.Should().Be("unrelated");
        ex.Message.Should().Contain(nameof(OtherMessage));
    }

    [Fact]
    public async Task WaitForAsync_timeout_exception_message_mentions_no_messages_when_bucket_empty()
    {
        // given
        var store = new MessageObservationStore();

        MessageObservationTimeoutException? ex = null;

        try
        {
            await store.WaitForAsync(
                typeof(SimpleMessage),
                MessageObservationType.Faulted,
                predicate: null,
                timeout: TimeSpan.FromMilliseconds(50),
                AbortToken
            );
        }
        catch (MessageObservationTimeoutException caught)
        {
            ex = caught;
        }

        // then
        ex.Should().NotBeNull();
        ex!.ObservedMessages.Should().BeEmpty();
        ex.Message.Should().Contain("No messages were");
    }

    // --- External cancellation ---

    [Fact]
    public async Task WaitForAsync_propagates_external_cancellation_as_OperationCanceledException()
    {
        // given
        var store = new MessageObservationStore();
        using var cts = new CancellationTokenSource();

        var waitTask = store.WaitForAsync(
            typeof(SimpleMessage),
            MessageObservationType.Consumed,
            predicate: null,
            timeout: TimeSpan.FromSeconds(30),
            cts.Token
        );

        // when
        await Task.Delay(20, AbortToken);
        await cts.CancelAsync();

        // then — must be OperationCanceledException, NOT MessageObservationTimeoutException
        await waitTask.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Clear ---

    [Fact]
    public void Clear_empties_all_collections()
    {
        // given
        var store = new MessageObservationStore();
        store.Record(_MakeMessage(typeof(SimpleMessage), "p1"), MessageObservationType.Published);
        store.Record(_MakeMessage(typeof(SimpleMessage), "c1"), MessageObservationType.Consumed);
        store.Record(_MakeMessage(typeof(SimpleMessage), "f1"), MessageObservationType.Faulted);

        // when
        store.Clear();

        // then
        store.Published.Should().BeEmpty();
        store.Consumed.Should().BeEmpty();
        store.Faulted.Should().BeEmpty();
    }

    // --- Helpers ---

    private class SimpleMessage
    {
        public string? Value { get; set; }
    }

    private sealed class DerivedMessage : SimpleMessage { }

    private sealed class OtherMessage { }
}
