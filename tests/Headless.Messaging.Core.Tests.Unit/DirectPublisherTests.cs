// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DirectPublisherTests : TestBase
{
    private sealed record TestMessage(string Value);

    private sealed record UnmappedMessage(int Id);

    [Fact]
    public async Task should_resolve_topic_from_mapping()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("test.topic");
    }

    [Fact]
    public async Task should_resolve_topic_from_conventions_when_no_explicit_mapping()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.ConfigureConventions(conventions => conventions.TopicNaming = TopicNamingConvention.KebabCase);

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("test-message");
    }

    [Fact]
    public async Task should_throw_when_no_topic_mapping()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        var act = () => publisher.PublishAsync(new UnmappedMessage(42), AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*No topic mapping found*UnmappedMessage*");
    }

    [Fact]
    public async Task should_throw_when_message_is_null()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";
        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        var act = () => publisher.PublishAsync<TestMessage>(null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_apply_topic_prefix()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions { TopicNamePrefix = "myapp" };
        options.TopicMappings[typeof(TestMessage)] = "events";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].GetName().Should().Be("myapp.events");
    }

    [Fact]
    public async Task should_throw_publisher_sent_failed_exception_on_transport_failure()
    {
        // given
        var testTransport = new TestTransport { ShouldFail = true };
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), AbortToken);

        // then
        await act.Should().ThrowAsync<Headless.Messaging.PublisherSentFailedException>();
    }

    [Fact]
    public async Task should_throw_when_transport_throws_exception()
    {
        // given
        var testTransport = new TestTransport
        {
            ExceptionToThrow = new InvalidOperationException("Transport unavailable"),
        };
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Transport unavailable");
    }

    [Fact]
    public async Task should_generate_standard_headers()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), AbortToken);

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
    public async Task should_use_provided_message_id_when_specified_in_headers()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);
        var customHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "custom-id-123",
        };

        // when
        await publisher.PublishAsync(new TestMessage("test"), customHeaders, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.MessageId].Should().Be("custom-id-123");
    }

    [Fact]
    public async Task should_use_provided_correlation_id_when_specified_in_headers()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);
        var customHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.CorrelationId] = "corr-123",
            [Headers.CorrelationSequence] = "5",
        };

        // when
        await publisher.PublishAsync(new TestMessage("test"), customHeaders, AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Headers[Headers.CorrelationId].Should().Be("corr-123");
        testTransport.SentMessages[0].Headers[Headers.CorrelationSequence].Should().Be("5");
    }

    [Fact]
    public async Task should_serialize_message_content()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test-value"), AbortToken);

        // then
        testTransport.SentMessages.Should().HaveCount(1);
        testTransport.SentMessages[0].Body.Length.Should().BeGreaterThan(0);
        var bodyString = System.Text.Encoding.UTF8.GetString(testTransport.SentMessages[0].Body.Span);
        bodyString.Should().Contain("test-value");
    }

    [Fact]
    public async Task should_respect_cancellation_token()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";
        var publisher = _CreateDirectPublisher(testTransport, options);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () => publisher.PublishAsync(new TestMessage("test"), cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        testTransport.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task should_call_transport_exactly_once()
    {
        // given
        var testTransport = new TestTransport();
        var options = new MessagingOptions();
        options.TopicMappings[typeof(TestMessage)] = "test.topic";

        var publisher = _CreateDirectPublisher(testTransport, options);

        // when
        await publisher.PublishAsync(new TestMessage("test"), AbortToken);

        // then
        testTransport.SendCallCount.Should().Be(1);
    }

    [Fact]
    public void should_register_as_scoped_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // when
        services.BuildServiceProvider();

        // then
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDirectPublisher));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void should_resolve_direct_publisher_from_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMessages(opt =>
        {
            opt.UseInMemoryMessageQueue();
            opt.UseInMemoryStorage();
        });

        // when
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // then
        var publisher = scope.ServiceProvider.GetService<IDirectPublisher>();
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<DirectPublisher>();
    }

    private static IDirectPublisher _CreateDirectPublisher(ITransport transport, MessagingOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(transport);
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator, SnowflakeIdLongIdGenerator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(options.JsonSerializerOptions);
        services.AddScoped<IDirectPublisher, DirectPublisher>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDirectPublisher>();
    }

    /// <summary>
    /// Test transport that captures sent messages for verification.
    /// </summary>
    private sealed class TestTransport : ITransport
    {
        private readonly ConcurrentBag<TransportMessage> _sentMessages = [];
        private int _sendCallCount;

        public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");
        public bool ShouldFail { get; init; }
        public Exception? ExceptionToThrow { get; init; }
        public int SendCallCount => _sendCallCount;
        public IReadOnlyList<TransportMessage> SentMessages => [.. _sentMessages];

        public Task<OperateResult> SendAsync(TransportMessage message)
        {
            Interlocked.Increment(ref _sendCallCount);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (ShouldFail)
            {
                return Task.FromResult(OperateResult.Failed(new Exception("Transport failure")));
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
