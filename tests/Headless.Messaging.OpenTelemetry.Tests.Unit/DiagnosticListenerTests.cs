// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Testing.Tests;
using Headless.Messaging.OpenTelemetry;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class DiagnosticListenerTests : TestBase
{
    [Fact]
    public void should_have_correct_source_name()
    {
        // then
        DiagnosticListener.SourceName.Should().Be("Headless.Messaging.OpenTelemetry");
    }

    [Fact]
    public void should_create_diagnostic_listener_without_metrics()
    {
        // given/when
        var listener = new DiagnosticListener();

        // then - no exception
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_create_diagnostic_listener_with_metrics()
    {
        // given
        using var metrics = new MessagingMetrics();

        // when
        var listener = new DiagnosticListener(metrics);

        // then
        listener.Should().NotBeNull();
    }

    [Fact]
    public void should_not_throw_on_completed()
    {
        // given
        var listener = new DiagnosticListener();

        // when/then
        var act = () => listener.OnCompleted();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_error()
    {
        // given
        var listener = new DiagnosticListener();

        // when/then
        var act = () => listener.OnError(new InvalidOperationException("Test"));
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_unknown_event_gracefully()
    {
        // given
        var listener = new DiagnosticListener();

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", new { Data = "test" }));

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_null_event_value_gracefully()
    {
        // given
        var listener = new DiagnosticListener();

        // when
        var act = () => listener.OnNext(new KeyValuePair<string, object?>("UnknownEvent", null));

        // then
        act.Should().NotThrow();
    }
}
