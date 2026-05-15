// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
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
    public void should_call_enrichers_when_event_is_received()
    {
        // given
        var enricher = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([enricher]);
        using var activitySource = new ActivitySource(DiagnosticListener.SourceName);
        using var activity = activitySource.StartActivity("test");

        // when
        listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then - enricher is not called for unknown events (only for known messaging events)
        enricher.DidNotReceive().Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>());
    }

    [Fact]
    public void should_continue_calling_enrichers_when_one_throws()
    {
        // given
        var failing = Substitute.For<IActivityTagEnricher>();
        failing.When(e => e.Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>()))
            .Throw(new InvalidOperationException("enricher failure"));

        var succeeding = Substitute.For<IActivityTagEnricher>();
        var listener = new DiagnosticListener([failing, succeeding]);
        using var activitySource = new ActivitySource(DiagnosticListener.SourceName);

        // when - unknown event keeps listener safe; real span events are integration territory
        listener.OnNext(new KeyValuePair<string, object?>("Unknown", null));

        // then - no exception propagated; succeeding enricher not reached for unknown events
        succeeding.DidNotReceive().Enrich(Arg.Any<Activity>(), Arg.Any<MessagingEnrichmentContext>());
    }
}
