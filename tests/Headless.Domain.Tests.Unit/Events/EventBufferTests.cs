// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Testing.Tests;

namespace Tests.Events;

public sealed class EventBufferTests : TestBase
{
    [Fact]
    public void should_capture_distinct_occurrences_and_retain_events_raised_after_snapshot()
    {
        var buffer = new EventBuffer();
        var payload = new Message("same payload");
        using var scope = EventEmissionScope.Begin(new EventEmissionContext("root", "cause", "tenant"));
        buffer.Add(payload);
        var batch = buffer.Snapshot();
        buffer.Add(payload);
        var pending = buffer.Snapshot();

        batch.Should().ContainSingle();
        pending.Should().HaveCount(2);
        pending[0].EventId.Should().NotBe(pending[1].EventId);
        pending[0].Payload.Should().BeSameAs(payload);
        pending[0].CorrelationId.Should().Be("root");
        pending[0].CausationId.Should().Be("cause");
        pending[0].TenantId.Should().Be("tenant");

        buffer.Clear(batch);
        buffer.Clear(batch);
        buffer.Snapshot().Should().Equal(pending[1]);
        pending.Should().HaveCount(2);
        buffer.Clear();
        buffer.Snapshot().Should().BeEmpty();
        batch.Should().ContainSingle();
    }

    [Fact]
    public void should_preserve_forwarded_metadata_and_support_heterogeneous_payloads()
    {
        var buffer = new EventBuffer();
        var forwarded = new EventContext<Message>(new Message("forwarded"), "event", "root", "cause", "tenant");
        var erased = new EventContext<object>(new object(), "other", "other-root");
        using var scope = EventEmissionScope.Begin(new EventEmissionContext("different-root", "different-cause"));
        buffer.Add(forwarded);
        buffer.Add(erased);

        var snapshot = buffer.Snapshot();
        snapshot.Should().HaveCount(2);
        snapshot[0].Should().BeEquivalentTo(forwarded);
        snapshot[0].Payload.Should().BeSameAs(forwarded.Payload);
        snapshot[1].Should().BeSameAs(erased);
    }

    [Fact]
    public void should_clear_by_ordinal_event_identity_instead_of_payload_or_envelope_reference()
    {
        var buffer = new EventBuffer();
        var payload = new Message("shared");
        buffer.Add(new EventContext<Message>(payload, "event", "root"));
        buffer.Add(new EventContext<Message>(payload, "EVENT", "root"));
        buffer.Clear([new EventContext<object>(new object(), "event", "another-root")]);
        buffer.Snapshot().Should().ContainSingle().Which.EventId.Should().Be("EVENT");
    }

    [Fact]
    public void should_support_empty_buffers_and_reuse_after_clear()
    {
        var buffer = new EventBuffer();
        buffer.Snapshot().Should().BeEmpty();
        buffer.Clear([]);
        buffer.Clear();
        buffer.Add(new Message("first"));
        buffer.Clear();
        buffer.Add(new Message("second"));
        var occurrence = buffer.Snapshot().Should().ContainSingle().Subject;
        occurrence.Payload.Should().BeEquivalentTo(new Message("second"));
        occurrence.CorrelationId.Should().Be(occurrence.EventId);
        occurrence.CausationId.Should().BeNull();
        occurrence.TenantId.Should().BeNull();
    }

    [Fact]
    public void should_reject_null_inputs_without_changing_pending_events()
    {
        var buffer = new EventBuffer();
        buffer.Add(new Message("pending"));
        var snapshot = buffer.Snapshot();
        Action nullPayload = () => buffer.Add((object)null!);
        Action nullContext = () => buffer.Add((EventContext<Message>)null!);
        Action nullBatch = () => buffer.Clear(null!);

        nullPayload.Should().Throw<ArgumentNullException>();
        nullContext.Should().Throw<ArgumentNullException>();
        nullBatch.Should().Throw<ArgumentNullException>();
        buffer.Snapshot().Should().Equal(snapshot);
    }

    private sealed record Message(string Value);
}
