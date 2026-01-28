// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.OpenTelemetry;
using Headless.Testing.Tests;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class MessagingInstrumentationTests : TestBase
{
    [Fact]
    public void should_create_instrumentation_with_diagnostic_listener()
    {
        // given
        var diagnosticListener = new DiagnosticListener();

        // when
        using var instrumentation = new MessagingInstrumentation(diagnosticListener);

        // then - no exception
    }

    [Fact]
    public void should_create_instrumentation_with_metrics()
    {
        // given
        var diagnosticListener = new DiagnosticListener();
        using var metrics = new MessagingMetrics();

        // when
        using var instrumentation = new MessagingInstrumentation(diagnosticListener, metrics);

        // then - no exception
    }

    [Fact]
    public void should_dispose_subscriber_and_metrics()
    {
        // given
        var diagnosticListener = new DiagnosticListener();
        using var metrics = new MessagingMetrics();
        using var instrumentation = new MessagingInstrumentation(diagnosticListener, metrics);

        // when
        var act = () => instrumentation.Dispose();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_multiple_dispose_calls()
    {
        // given
        var diagnosticListener = new DiagnosticListener();
        using var metrics = new MessagingMetrics();
        using var instrumentation = new MessagingInstrumentation(diagnosticListener, metrics);

        // when
        instrumentation.Dispose();
        var act = () => instrumentation.Dispose();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_dispose_without_metrics()
    {
        // given
        var diagnosticListener = new DiagnosticListener();
        using var instrumentation = new MessagingInstrumentation(diagnosticListener);

        // when
        var act = () => instrumentation.Dispose();

        // then
        act.Should().NotThrow();
    }
}
