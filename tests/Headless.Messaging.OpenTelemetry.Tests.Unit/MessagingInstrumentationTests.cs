// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Diagnostics;
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

    [Fact]
    public void should_preserve_public_diagnostic_contract_names()
    {
        MessageDiagnosticListenerNames.DiagnosticListenerName.Should().Be("MessagingDiagnosticListener");
        MessageDiagnosticListenerNames
            .BeforePublishMessageStore.Should()
            .Be("Headless.Messages.WritePublishMessageStoreBefore");
        MessageDiagnosticListenerNames
            .AfterPublishMessageStore.Should()
            .Be("Headless.Messages.WritePublishMessageStoreAfter");
        MessageDiagnosticListenerNames
            .ErrorPublishMessageStore.Should()
            .Be("Headless.Messages.WritePublishMessageStoreError");
        MessageDiagnosticListenerNames.BeforePublish.Should().Be("Headless.Messages.WritePublishBefore");
        MessageDiagnosticListenerNames.AfterPublish.Should().Be("Headless.Messages.WritePublishAfter");
        MessageDiagnosticListenerNames.ErrorPublish.Should().Be("Headless.Messages.WritePublishError");
        MessageDiagnosticListenerNames.BeforeConsume.Should().Be("Headless.Messages.WriteConsumeBefore");
        MessageDiagnosticListenerNames.AfterConsume.Should().Be("Headless.Messages.WriteConsumeAfter");
        MessageDiagnosticListenerNames.ErrorConsume.Should().Be("Headless.Messages.WriteConsumeError");
        MessageDiagnosticListenerNames
            .BeforeSubscriberInvoke.Should()
            .Be("Headless.Messages.WriteSubscriberInvokeBefore");
        MessageDiagnosticListenerNames
            .AfterSubscriberInvoke.Should()
            .Be("Headless.Messages.WriteSubscriberInvokeAfter");
        MessageDiagnosticListenerNames
            .ErrorSubscriberInvoke.Should()
            .Be("Headless.Messages.WriteSubscriberInvokeError");
        MessageDiagnosticListenerNames.MetricListenerName.Should().Be("Headless.Messages.EventCounter");
        MessageDiagnosticListenerNames.PublishedPerSec.Should().Be("published-per-second");
        MessageDiagnosticListenerNames.ConsumePerSec.Should().Be("consume-per-second");
        MessageDiagnosticListenerNames.InvokeSubscriberPerSec.Should().Be("invoke-subscriber-per-second");
        MessageDiagnosticListenerNames.InvokeSubscriberElapsedMs.Should().Be("invoke-subscriber-elapsed-ms");
    }
}
