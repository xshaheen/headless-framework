// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class BusTests : TestBase
{
    private sealed record TestMessage(string Value);

    private sealed record UnmappedMessage(int Id);

    [Fact]
    public async Task should_resolve_topic_from_mapping()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("test.messageName");
    }

    [Fact]
    public async Task should_resolve_publish_message_name_from_registry_mapping()
    {
        // given — registry maps TestMessage to a domain-specific name; publish path must agree
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options, mappedMessageName: "orders.placed");

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), cancellationToken: AbortToken);

        // then — transport message name must equal the registry-sourced mapping, not a convention-derived fallback
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("orders.placed");
    }

    [Fact]
    public async Task should_resolve_topic_from_conventions_when_no_explicit_mapping()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions { Conventions = { MessageNaming = MessageNamingConvention.KebabCase } };

        var publisher = _CreateBus(testTransport, options, mappedMessageName: null);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("test-message");
    }

    [Fact]
    public async Task should_resolve_topic_from_default_convention_when_no_mapping_exists()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();
        var publisher = _CreateBus(testTransport, options, mappedMessageName: null);

        // when
        await publisher.PublishAsync(new UnmappedMessage(42), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be(nameof(UnmappedMessage));
    }

    [Fact]
    public async Task should_allow_null_payload()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options, mappedMessageName: "events");

        // when
        await publisher.PublishAsync<TestMessage>(null, cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task should_apply_topic_prefix()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions { MessageNamePrefix = "myapp" };

        var publisher = _CreateBus(testTransport, options, mappedMessageName: "events");

        // when
        await publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("myapp.events");
    }

    [Fact]
    public async Task should_throw_publisher_sent_failed_exception_on_transport_failure()
    {
        // given
        await using var testTransport = new TestTransport { ShouldFail = true };
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_throw_when_transport_throws_exception()
    {
        // given
        await using var testTransport = new TestTransport
        {
            ExceptionToThrow = new InvalidOperationException("Transport unavailable"),
        };

        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Transport unavailable");
    }

    [Fact]
    public async Task should_generate_standard_headers()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        var headers = testTransport.SentMessages[0].Headers;
        headers.Should().ContainKey(Headers.MessageId);
        headers.Should().ContainKey(Headers.CorrelationId);
        headers.Should().ContainKey(Headers.SentTime);
        headers.Should().ContainKey(Headers.MessageName);
        headers[Headers.CorrelationSequence].Should().Be("0");
    }

    [Fact]
    public async Task should_use_publish_options_overrides_for_message_metadata()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            MessageId = "custom-id-123",
            CorrelationId = "corr-123",
            CorrelationSequence = 5,
            CallbackName = "callback.messageName",
        };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.MessageId].Should().Be("custom-id-123");
        testTransport.SentMessages[0].Headers[Headers.CorrelationId].Should().Be("corr-123");
        testTransport.SentMessages[0].Headers[Headers.CorrelationSequence].Should().Be("5");
        testTransport.SentMessages[0].Headers[Headers.CallbackName].Should().Be("callback.messageName");
    }

    [Fact]
    public async Task should_reject_callback_name_values_with_control_characters()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { CallbackName = "callbacks\r\nnext" };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{Headers.CallbackName}*control characters*");
    }

    [Fact]
    public async Task should_allow_maximum_supported_message_id_length()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { MessageId = new string('m', MessageOptions.MessageIdMaxLength) };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.MessageId].Should().HaveLength(MessageOptions.MessageIdMaxLength);
    }

    [Fact]
    public async Task should_reject_message_id_values_longer_than_the_supported_limit()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { MessageId = new string('m', MessageOptions.MessageIdMaxLength + 1) };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("messageId")
            .WithMessage($"*{MessageOptions.MessageIdMaxLength} characters or fewer*");
    }

    [Fact]
    public async Task should_reject_message_id_values_with_control_characters()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { MessageId = "msg\r\n1" };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{Headers.MessageId}*control characters*");
    }

    [Fact]
    public async Task should_allow_explicit_topic_from_publish_options()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { MessageName = "explicit.messageName" };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("explicit.messageName");
    }

    [Theory]
    [InlineData(".leading-dot")]
    [InlineData("trailing-dot.")]
    [InlineData("double..dot")]
    [InlineData("bad/slash")]
    public async Task should_reject_invalid_explicit_message_name_from_publish_options(string messageName)
    {
        // given
        await using var testTransport = new TestTransport();
        var publisher = _CreateBus(testTransport, new MessagingOptions());
        var publishOptions = new PublishOptions { MessageName = messageName };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(messageName));
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_throw_when_reserved_headers_are_supplied_as_custom_headers()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        var publishOptions = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageName] = "forbidden.messageName",
            },
        };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Fact]
    public async Task should_reject_custom_header_names_with_control_characters()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["bad\r\nheader"] = "value" },
        };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot contain control characters*");
    }

    [Fact]
    public async Task should_reject_custom_header_values_with_control_characters()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { ["x-custom"] = "bad\r\nvalue" },
        };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*x-custom*control characters*");
    }

    [Fact]
    public async Task should_stamp_tenant_id_header_when_typed_property_is_set_and_raw_header_is_absent()
    {
        // given (case a: typed-only)
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { TenantId = "acme" };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public async Task should_emit_tenant_id_header_when_typed_property_and_raw_header_agree()
    {
        // given (case c: both set, equal)
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            TenantId = "acme",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "acme" },
        };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.TenantId].Should().Be("acme");
    }

    [Fact]
    public async Task should_reject_publish_when_raw_tenant_id_header_is_set_without_typed_property()
    {
        // given (case b: raw-only — the typed property must be the source of truth)
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "evil" },
        };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{Headers.TenantId}' is reserved*{nameof(PublishOptions.TenantId)}*");
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_publish_when_typed_property_and_raw_header_disagree()
    {
        // given (case d: both set, disagree)
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            TenantId = "acme",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "acme-evil" },
        };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'acme'*'acme-evil'*");
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_omit_tenant_id_header_when_typed_property_is_null()
    {
        // given (case: neither set)
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers.Should().NotContainKey(Headers.TenantId);
    }

    [Fact]
    public async Task should_treat_whitespace_raw_tenant_id_header_as_unset()
    {
        // given: whitespace raw header with no typed property — symmetric with consume-side leniency
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions
        {
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.TenantId] = "   " },
        };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers.Should().NotContainKey(Headers.TenantId);
    }

    [Fact]
    public async Task should_reject_whitespace_typed_tenant_id()
    {
        // given: whitespace typed value — must throw because TenantId has no auto-generated default
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { TenantId = "   " };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("tenantId");
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_typed_tenant_id_with_control_characters()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { TenantId = "acme\r\ncorp" };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{Headers.TenantId}*control characters*");
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_reject_oversized_typed_tenant_id()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var publishOptions = new PublishOptions { TenantId = new string('t', MessageOptions.TenantIdMaxLength + 1) };

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("tenantId")
            .WithMessage($"*{MessageOptions.TenantIdMaxLength} characters or fewer*");
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_allow_maximum_supported_tenant_id_length()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);
        var maxTenantId = new string('t', MessageOptions.TenantIdMaxLength);
        var publishOptions = new PublishOptions { TenantId = maxTenantId };

        // when
        await publisher.PublishAsync(new TestMessage("test"), publishOptions, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.TenantId].Should().Be(maxTenantId);
    }

    [Fact]
    public async Task should_serialize_message_content()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), cancellationToken: AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Body.Length.Should().BeGreaterThan(0);
        var bodyString = Encoding.UTF8.GetString(testTransport.SentMessages[0].Body.Span);
        bodyString.Should().Contain("test-value");
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        // ReSharper disable once AccessToDisposedClosure
        var act = () => publisher.PublishAsync(new TestMessage("test"), cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_call_transport_exactly_once()
    {
        // given
        await using var testTransport = new TestTransport();
        var options = new MessagingOptions();

        var publisher = _CreateBus(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        testTransport.SendCallCount.Should().Be(1);
    }

    [Fact]
    public void should_register_as_singleton_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        // when
        using var provider = services.BuildServiceProvider();

        // then
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBus));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task should_resolve_bus_from_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then - singleton can be resolved directly without scope
        var publisher = provider.GetService<IBus>();
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<Bus>();
    }

    [Fact]
    public async Task should_propagate_serializer_exception_on_publish()
    {
        // given
        var serializer = Substitute.For<ISerializer>();
        serializer
            .SerializeToTransportMessageAsync(Arg.Any<Message>())
            .Returns<ValueTask<TransportMessage>>(_ => throw new InvalidOperationException("Serializer failure"));

        var options = new MessagingOptions();
        var publisher = _CreateBusWithSerializer(serializer, options);

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Serializer failure");
    }

    private static IBus _CreateBusWithSerializer(
        ISerializer serializer,
        MessagingOptions options,
        IBusTransport? transport = null,
        string? mappedMessageName = "test.messageName"
    )
    {
        var optionsAccessor = Options.Create(options);
        var registry = _CreateRegistry(mappedMessageName);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            registry,
            new NullCurrentTenant()
        );
        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());

        return new Bus(
            serializer,
            transport ?? new TestTransport(),
            publishRequestFactory,
            pipeline,
            TimeProvider.System
        );
    }

    private static Bus _CreateBus(
        IBusTransport transport,
        MessagingOptions options,
        ICurrentTenant? currentTenant = null,
        string? mappedMessageName = "test.messageName"
    )
    {
        var optionsAccessor = Options.Create(options);
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var registry = _CreateRegistry(mappedMessageName);

        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            registry,
            currentTenant ?? new NullCurrentTenant()
        );

        var pipeline = new PublishMiddlewarePipeline(new ServiceCollection().BuildServiceProvider());
        return new Bus(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);
    }

    private static ConsumerRegistry _CreateRegistry(string? mappedMessageName)
    {
        var registry = new ConsumerRegistry();
        if (mappedMessageName is not null)
        {
            registry.RegisterMessageName(typeof(TestMessage), mappedMessageName);
        }

        return registry;
    }

    /// <summary>
    /// Test transport that captures sent messages for verification.
    /// </summary>
    private sealed class TestTransport : IBusTransport
    {
        private readonly ConcurrentBag<TransportMessage> _sentMessages = [];
        private int _sendCallCount;

        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");
        public bool ShouldFail { get; init; }
        public Exception? ExceptionToThrow { get; init; }
        public int SendCallCount => _sendCallCount;
        public IReadOnlyList<TransportMessage> SentMessages => [.. _sentMessages];

        public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sendCallCount);

            cancellationToken.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (ShouldFail)
            {
                return Task.FromResult(OperateResult.Failed(new InvalidOperationException("Transport failure")));
            }

            _sentMessages.Add(message);
            return Task.FromResult(OperateResult.Success);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
