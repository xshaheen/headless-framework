// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Domain;
using Headless.Testing.Tests;

namespace Tests.Events;

public sealed class EventEmissionScopeTests : TestBase
{
    private sealed record Fact(string Value);

    private sealed class TestAggregate : AggregateRoot<int>
    {
        public void Raise(object payload) => AddDomainEvent(payload);
    }

    [Fact]
    public void should_reject_invalid_occurrences_and_scopes_without_changing_ambient_context()
    {
        Action nullPayload = () => EventContext.Capture<object>(null!);
        Action nullIdentity = () => new EventContext<Fact>(new Fact("payload"), null!, "root");
        Action blankIdentity = () => new EventContext<Fact>(new Fact("payload"), " ", "root");
        Action blankCorrelation = () => new EventEmissionContext(" ");
        Action nullScope = () => EventEmissionScope.Begin((EventEmissionContext)null!);
        Action nullParent = () => EventEmissionScope.Begin((EventContext<Fact>)null!);
        nullPayload.Should().Throw<ArgumentNullException>();
        nullIdentity.Should().Throw<ArgumentNullException>();
        blankIdentity.Should().Throw<ArgumentException>();
        blankCorrelation.Should().Throw<ArgumentException>();
        nullScope.Should().Throw<ArgumentNullException>();
        nullParent.Should().Throw<ArgumentNullException>();
        EventEmissionScope.Current.Should().BeNull();
    }

    [Fact]
    public void should_capture_two_occurrences_of_one_payload_and_clear_only_saved_batch()
    {
        var aggregate = new TestAggregate { Id = 1 };
        var payload = new Fact("same fact shape");
        aggregate.Raise(payload);
        var batch = aggregate.GetDomainEvents();
        aggregate.Raise(payload);
        var occurrences = aggregate.GetDomainEvents();

        batch.Should().ContainSingle();
        occurrences.Should().HaveCount(2);
        occurrences[0].EventId.Should().NotBe(occurrences[1].EventId);
        occurrences[0].Payload.Should().BeSameAs(payload);
        aggregate.ClearDomainEvents(batch);
        aggregate.GetDomainEvents().Should().Equal(occurrences[1]);
    }

    [Fact]
    public void should_forward_concrete_envelopes_through_both_emitter_buffers_without_recapture()
    {
        var aggregate = new TestAggregate { Id = 1 };
        EventContext<Fact> context;
        using (EventEmissionScope.Begin(new EventEmissionContext("root", "cause", "tenant")))
        {
            context = EventContext.Capture(new Fact("forwarded"));
        }

        ((IDomainEventEmitter)aggregate).AddDomainEvent(context);
        ((IIntegrationEventEmitter)aggregate).AddIntegrationEvent(context);

        aggregate.GetDomainEvents().Should().ContainSingle().Which.Should().BeEquivalentTo(context);
        aggregate.GetIntegrationEvents().Should().ContainSingle().Which.Should().BeEquivalentTo(context);
        aggregate.GetDomainEvents()[0].Payload.Should().BeSameAs(context.Payload);
        aggregate.GetIntegrationEvents()[0].Payload.Should().BeSameAs(context.Payload);
    }

    [Fact]
    public void should_root_business_correlation_independently_of_activity()
    {
        using var activity = new Activity("trace-only").Start();
        var occurrence = EventContext.Capture<object>(new Fact("root"));
        occurrence.CorrelationId.Should().Be(occurrence.EventId);
        occurrence.CausationId.Should().BeNull();
        occurrence.TenantId.Should().BeNull();
        occurrence.CorrelationId.Should().NotBe(activity.Id);
    }

    [Fact]
    public void should_restore_parent_after_exception_and_reject_out_of_order_disposal()
    {
        var parent = EventEmissionScope.Begin(new EventEmissionContext("root", "message", "tenant"));
        var child = EventEmissionScope.Begin(new EventEmissionContext("nested", "child"));
        Action invalidDispose = parent.Dispose;
        invalidDispose.Should().Throw<InvalidOperationException>();
        EventEmissionScope.Current!.CorrelationId.Should().Be("nested");
        child.Dispose();
        EventEmissionScope.Current!.CorrelationId.Should().Be("root");

        Action failingOperation = () =>
        {
            using var nested = EventEmissionScope.Begin(new EventEmissionContext("exception"));
            throw new InvalidOperationException("handler");
        };
        failingOperation.Should().Throw<InvalidOperationException>();
        EventEmissionScope.Current!.CorrelationId.Should().Be("root");
        parent.Dispose();
        EventEmissionScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_isolate_parallel_flows_and_snapshot_lineage_at_raise()
    {
        using var outer = EventEmissionScope.Begin(new EventEmissionContext("outer"));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<EventContext<object>> CaptureAsync(string tenant)
        {
            using var scope = EventEmissionScope.Begin(new EventEmissionContext(tenant, "cause", tenant));
            if (Interlocked.Increment(ref arrived) == 2)
            {
                ready.SetResult();
            }

            await ready.Task.WaitAsync(AbortToken);
            return EventContext.Capture<object>(new Fact(tenant));
        }

        var occurrences = await Task.WhenAll(CaptureAsync("one"), CaptureAsync("two"));
        occurrences.Select(occurrence => occurrence.TenantId).Should().Equal("one", "two");
        occurrences.Select(occurrence => occurrence.CorrelationId).Should().Equal("one", "two");
        EventEmissionScope.Current!.CorrelationId.Should().Be("outer");
    }
}
