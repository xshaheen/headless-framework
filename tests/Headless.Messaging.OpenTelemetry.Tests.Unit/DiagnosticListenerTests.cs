// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.OpenTelemetry;
using Headless.Testing.Tests;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class DiagnosticListenerTests : TestBase
{
    [Fact]
    public void should_have_correct_source_name()
    {
        // then
        DiagnosticListener.SourceName.Should().Be("Headless.Messaging");
    }

    [Fact]
    public void should_create_diagnostic_listener_without_metrics()
    {
        // given/when
        var listener = new DiagnosticListener([]);

        // then - no exception
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_create_diagnostic_listener_with_metrics()
    {
        // given
        using var metrics = new MessagingMetrics();

        // when
        var listener = new DiagnosticListener([], metrics: metrics);

        // then
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_not_throw_on_completed()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when/then
        var act = () => listener.OnCompleted();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_error()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when/then
        var act = () => listener.OnError(new InvalidOperationException("Test"));
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_unknown_event_gracefully()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", new { Data = "test" }));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_null_event_value_gracefully()
    {
        // given
        var listener = new DiagnosticListener([]);

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_call_enrichers_when_event_key_is_unknown()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);

        // when
        listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then
        enricher.DidNotReceive().Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>());
    }

    [Fact]
    public void should_call_enrichers_when_persist_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        listener.OnNext(new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData));

        // then
        enricher.Received().Enrich(
            Arg.Any<Activity>(),
            Arg.Is<MessagingEnrichmentContext>(c => c.Kind == MessagingEventKind.Persist && c.MessageName == "order.created")
        );
    }

    [Fact]
    public void should_continue_calling_enrichers_when_one_throws_and_logger_is_null()
    {
        // given - null logger is the production path: OTel AddInstrumentation cannot inject ILogger
        var failing = Substitute.For<IActivityTagEnricher>();
        failing.When(e => e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>()))
            .Throw(new InvalidOperationException("enricher failure"));

        var succeeding = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([failing, succeeding]); // no logger
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        var act = () => listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then - exception is swallowed and the second enricher still runs
        act.Should().NotThrow();
        succeeding.Received().Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>());
    }

    [Fact]
    public void should_not_propagate_enricher_exception_when_logger_is_null()
    {
        // given - the critical production path: logger is null because OTel AddInstrumentation
        // cannot accept Func<IServiceProvider, T>, so the enricher exception must be swallowed
        var failing = Substitute.For<IActivityTagEnricher>();
        failing.When(e => e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>()))
            .Throw(new InvalidOperationException("enricher failure"));

        var listener = new DiagnosticListener([failing]); // no logger
        using var activityListener = _CreateActivityListener();
        var eventData = _CreatePubStoreEventData("order.created");

        // when
        var act = () => listener.OnNext(
            new KeyValuePair<string, object?>(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData)
        );

        // then - must not propagate; if it did, the diagnostic subscriber would crash and kill all observability
        act.Should().NotThrow();
    }

    private static ActivityListener _CreateActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DiagnosticListener.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MessageEventDataPubStore _CreatePubStoreEventData(string operation)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = operation,
        };

        return new MessageEventDataPubStore
        {
            Operation = operation,
            Message = new Message(headers, null),
            OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}
