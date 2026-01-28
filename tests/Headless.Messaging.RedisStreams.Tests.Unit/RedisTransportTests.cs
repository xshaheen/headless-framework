// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.RedisStreams;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="RedisTransport"/>.
/// </summary>
public sealed class RedisTransportTests : TestBase
{
    private readonly IRedisStreamManager _mockStreamManager;
    private readonly IOptions<MessagingRedisOptions> _options;
    private readonly RedisTransport _sut;

    public RedisTransportTests()
    {
        _mockStreamManager = Substitute.For<IRedisStreamManager>();
        _options = Options.Create(
            new MessagingRedisOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
        );
        var logger = LoggerFactory.CreateLogger<RedisTransport>();
        _sut = new RedisTransport(_mockStreamManager, _options, logger);
    }

    [Fact]
    public void should_return_correct_broker_address()
    {
        // when
        var address = _sut.BrokerAddress;

        // then
        address.Name.Should().Be("redis");
        address.Endpoint.Should().Be("localhost:6379");
    }

    [Fact]
    public async Task should_publish_message_to_stream()
    {
        // given
        var headers = new Dictionary<string, string?>
        {
            [Headers.MessageId] = "test-id-123",
            [Headers.MessageName] = "test-topic",
        };
        var body = """{"data":"test"}"""u8.ToArray();
        var message = new TransportMessage(headers, body);

        _mockStreamManager.PublishAsync(Arg.Any<string>(), Arg.Any<NameValueEntry[]>()).Returns(Task.CompletedTask);

        // when
        var result = await _sut.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        await _mockStreamManager.Received(1).PublishAsync("test-topic", Arg.Any<NameValueEntry[]>());
    }

    [Fact]
    public async Task should_return_failed_result_when_publish_throws()
    {
        // given
        var headers = new Dictionary<string, string?>
        {
            [Headers.MessageId] = "test-id-123",
            [Headers.MessageName] = "test-topic",
        };
        var message = new TransportMessage(headers, ReadOnlyMemory<byte>.Empty);

        var expectedException = new RedisException("Connection failed");
        _mockStreamManager.PublishAsync(Arg.Any<string>(), Arg.Any<NameValueEntry[]>()).ThrowsAsync(expectedException);

        // when
        var result = await _sut.SendAsync(message);

        // then
        result.Succeeded.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.InnerException.Should().BeSameAs(expectedException);
    }

    [Fact]
    public async Task should_include_message_headers_in_stream_entry()
    {
        // given
        var headers = new Dictionary<string, string?>
        {
            [Headers.MessageId] = "msg-001",
            [Headers.MessageName] = "orders.created",
            [Headers.CorrelationId] = "corr-xyz",
        };
        var body = """{"orderId":123}"""u8.ToArray();
        var message = new TransportMessage(headers, body);

        NameValueEntry[]? capturedEntries = null;
        _mockStreamManager
            .PublishAsync(Arg.Any<string>(), Arg.Do<NameValueEntry[]>(x => capturedEntries = x))
            .Returns(Task.CompletedTask);

        // when
        await _sut.SendAsync(message);

        // then
        capturedEntries.Should().NotBeNull();
        capturedEntries.Should().HaveCount(2); // headers + body
    }

    [Fact]
    public async Task should_dispose_without_error()
    {
        // when & then - DisposeAsync should complete without error
        var action = async () => await _sut.DisposeAsync();
        await action.Should().NotThrowAsync();
    }
}
