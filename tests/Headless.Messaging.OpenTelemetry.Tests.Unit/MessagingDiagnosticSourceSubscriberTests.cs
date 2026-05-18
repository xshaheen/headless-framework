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
        var handler = new DiagnosticListener([]);
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when
        subscriber.Subscribe();

        // then - no exception thrown
    }

    [Fact]
    public void should_dispose_properly()
    {
        // given
        var handler = new DiagnosticListener([]);
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
        var handler = new DiagnosticListener([]);
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
        var handler = new DiagnosticListener([]);
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when/then
        var act = () => subscriber.OnCompleted();
        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_on_error()
    {
        // given
        var handler = new DiagnosticListener([]);
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when/then
        var act = () => subscriber.OnError(new InvalidOperationException("Test error"));
        act.Should().NotThrow();
    }

    [Fact]
    public void should_only_subscribe_once()
    {
        // given
        var handler = new DiagnosticListener([]);
        using var subscriber = new MessagingDiagnosticSourceSubscriber(handler, isEnabledFilter: null);

        // when
        subscriber.Subscribe();
        subscriber.Subscribe();
        subscriber.Subscribe();

        // then - no exception, idempotent behavior
    }

    [Fact]
    public void should_deliver_events_exactly_once_when_subscribe_called_multiple_times()
    {
        // given - count how many times the handler factory is invoked for a single matching source.
        // If Subscribe() were not idempotent, multiple internal AllListeners subscriptions would each
        // forward the source through OnNext, causing the factory to fire more than once for one source.
        var factoryInvocations = 0;
        var handlerFactory = new Func<string, DiagnosticListener>(_ =>
        {
            Interlocked.Increment(ref factoryInvocations);
            return new DiagnosticListener([]);
        });

        using var subscriber = new MessagingDiagnosticSourceSubscriber(
            handlerFactory,
            value =>
                string.Equals(
                    MessageDiagnosticListenerNames.DiagnosticListenerName,
                    value.Name,
                    StringComparison.Ordinal
                ),
            isEnabledFilter: null
        );

        // when - calling Subscribe() multiple times must be idempotent
        subscriber.Subscribe();
        subscriber.Subscribe();
        subscriber.Subscribe();

        // and a single matching source is published to AllListeners
        using var source = new System.Diagnostics.DiagnosticListener(
            MessageDiagnosticListenerNames.DiagnosticListenerName
        );

        // then - the handler factory must be invoked exactly once for the single source,
        // regardless of how many times Subscribe() was called.
        factoryInvocations.Should().Be(1);
    }

    [Fact]
    public void should_create_with_handler_factory()
    {
        // given
        Func<string, DiagnosticListener> handlerFactory = _ => new DiagnosticListener([]);
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
        var handler = new DiagnosticListener([]);
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
