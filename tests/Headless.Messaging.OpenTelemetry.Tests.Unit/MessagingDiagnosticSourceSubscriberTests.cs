// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Diagnostics;
using Headless.Messaging.OpenTelemetry;
using Headless.Testing.Tests;
using DiagnosticListener = Headless.Messaging.OpenTelemetry.DiagnosticListener;

namespace Tests;

public sealed class MessagingDiagnosticSourceSubscriberTests : TestBase
{
    [Fact]
    public void should_subscribe_to_diagnostic_source()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when
        subscriber.Subscribe();

        // then - no exception thrown
    }

    [Fact]
    public void should_dispose_properly()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);
        subscriber.Subscribe();

        // when/then - no exception
        var act = () => subscriber.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_handle_multiple_dispose_calls()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);
        subscriber.Subscribe();

        // when
        subscriber.Dispose();
        var act = () => subscriber.Dispose();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_completed()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when/then
        var act = () => subscriber.OnCompleted();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_error()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when/then
        var act = () => subscriber.OnError(new InvalidOperationException("Test error"));
        act.Should().NotThrow();
    }

    [Fact]
    public void should_only_subscribe_once()
    {
        // given
        var handler = new DiagnosticListener();
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when
        subscriber.Subscribe();
        subscriber.Subscribe();
        subscriber.Subscribe();

        // then - no exception, idempotent behavior
    }

    [Fact]
    public void should_create_with_handler_factory()
    {
        // given
        Func<string, DiagnosticListener> handlerFactory = _ => new DiagnosticListener();
        Func<System.Diagnostics.DiagnosticListener, bool> diagnosticSourceFilter = source =>
            string.Equals(source.Name, MessageDiagnosticListenerNames.DiagnosticListenerName, StringComparison.Ordinal);

        // when
        using var subscriber = new MessagingDiagnosticSourceSubscriber(
            handlerFactory,
            diagnosticSourceFilter,
            isEnabledFilter: null
        );

        // then
        subscriber.Should().NotBeNull();
    }

    [Fact]
    public void should_create_with_is_enabled_filter()
    {
        // given
        var handler = new DiagnosticListener();
        Func<string, object?, object?, bool> isEnabledFilter = (_, _, _) => true;

        // when
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter);

        // then
        subscriber.Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_handler_factory_is_null()
    {
        // given
        Func<string, DiagnosticListener> handlerFactory = null!;
        Func<System.Diagnostics.DiagnosticListener, bool> diagnosticSourceFilter = _ => true;

        // when
        var act = () =>
            new MessagingDiagnosticSourceSubscriber(handlerFactory, diagnosticSourceFilter, isEnabledFilter: null);

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
