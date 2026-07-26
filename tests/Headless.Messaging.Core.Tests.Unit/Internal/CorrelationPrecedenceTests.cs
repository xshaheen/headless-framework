// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

public sealed class CorrelationPrecedenceTests
{
    [Fact]
    public void should_prefer_explicit_correlation_over_selector()
    {
        // given
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(
            new TestMessage("selector-correlation"),
            new PublishOptions { CorrelationId = "explicit-correlation" }
        );

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("explicit-correlation");
    }

    [Fact]
    public void should_use_selector_correlation_when_explicit_correlation_is_absent()
    {
        // given
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(new TestMessage("selector-correlation"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("selector-correlation");
    }

    [Theory]
    [InlineData("explicit\r\ncorrelation", "explicit")]
    [InlineData("selector\r\ncorrelation", "selector")]
    [InlineData("ambient\r\ncorrelation", "ambient")]
    public void should_reject_correlation_values_with_control_characters(string correlationId, string source)
    {
        // given
        var accessor = string.Equals(source, "ambient", StringComparison.Ordinal)
            ? new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext(correlationId) }
            : null;
        var factory = string.Equals(source, "selector", StringComparison.Ordinal)
            ? _CreateFactory(selector: _ => correlationId)
            : _CreateFactory(accessor: accessor);
        var options = string.Equals(source, "explicit", StringComparison.Ordinal)
            ? new PublishOptions { CorrelationId = correlationId }
            : null;

        // when
        var act = () => factory.Create(new TestMessage(null), options);

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*headless-corr-id*control characters*");
    }

    [Fact]
    public void should_prefer_selector_correlation_over_ambient()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-value") };
        var factory = _CreateFactory(selector: static message => message.Correlation, accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage("selector-value"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("selector-value");
    }

    [Fact]
    public void should_use_ambient_correlation_when_explicit_and_selector_are_absent()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-correlation") };
        var factory = _CreateFactory(accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("ambient-correlation");
    }

    [Fact]
    public void should_default_correlation_to_message_id_when_no_source_is_available()
    {
        // given
        var factory = _CreateFactory();

        // when
        var prepared = factory.Create(new TestMessage(null));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
    }

    [Fact]
    public void should_not_invoke_selector_for_null_payload()
    {
        // given
        var invoked = false;
        var factory = _CreateFactory(selector: message =>
        {
            invoked = true;
            return message.Correlation;
        });

        // when
        var prepared = factory.Create<TestMessage>(null);

        // then
        invoked.Should().BeFalse();
        prepared.Message.Headers[Headers.CorrelationId].Should().Be(prepared.Message.Headers[Headers.MessageId]);
    }

    [Fact]
    public void should_treat_empty_selector_result_as_absent()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor { Current = _ConsumeContext("ambient-correlation") };
        var factory = _CreateFactory(selector: static _ => " ", accessor: accessor);

        // when
        var prepared = factory.Create(new TestMessage("ignored"));

        // then
        prepared.Message.Headers[Headers.CorrelationId].Should().Be("ambient-correlation");
    }

    [Fact]
    public void should_leave_traceparent_header_unchanged()
    {
        // given
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00";
        var factory = _CreateFactory(selector: static message => message.Correlation);

        // when
        var prepared = factory.Create(
            new TestMessage("selector-correlation"),
            new PublishOptions
            {
                Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.TraceParent] = traceParent,
                },
            }
        );

        // then
        prepared.Message.Headers[Headers.TraceParent].Should().Be(traceParent);
    }

    [Fact]
    public void should_wrap_selector_failure_with_message_type()
    {
        // given
        var factory = _CreateFactory(selector: static _ => throw new InvalidOperationException("boom"));

        // when
        var act = () => factory.Create(new TestMessage("ignored"));

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CorrelationFrom selector failed*TestMessage*")
            .WithInnerException<InvalidOperationException>();
    }

    private static MessagePublishRequestFactory _CreateFactory(
        Func<TestMessage, string?>? selector = null,
        IConsumeContextAccessor? accessor = null
    )
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "test.message");
        var registrations = selector is null
            ? Array.Empty<MessageRegistration>()
            :
            [
                new MessageRegistration(
                    typeof(TestMessage),
                    MessageLane.Bus,
                    "test.message",
                    message => selector((TestMessage)message),
                    new Dictionary<Type, object>(),
                    []
                ),
            ];

        return new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            registry,
            new NullCurrentTenant(),
            new MessageMetadataRegistry(registrations),
            accessor
        );
    }

    private static ConsumeContext<TestMessage> _ConsumeContext(string correlationId)
    {
        return new()
        {
            Message = new TestMessage(null),
            MessageId = "source-message",
            CorrelationId = correlationId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "source",
            IntentType = IntentType.Bus,
        };
    }

    private sealed record TestMessage(string? Correlation);
}
