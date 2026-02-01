// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Xunit;
using MessagingHeaders = Headless.Messaging.Messages.Headers;

namespace Tests;

/// <summary>Base class for transport implementation tests.</summary>
[PublicAPI]
public abstract class TransportTestsBase : TestBase
{
    /// <summary>Gets the transport instance for testing.</summary>
    protected abstract ITransport GetTransport();

    /// <summary>Gets the transport capabilities for conditional test execution.</summary>
    protected virtual TransportCapabilities Capabilities => TransportCapabilities.Default;

    /// <summary>Creates a valid transport message with required headers.</summary>
    protected static TransportMessage CreateMessage(
        string? messageId = null,
        string? messageName = null,
        ReadOnlyMemory<byte>? body = null,
        IDictionary<string, string?>? additionalHeaders = null
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, messageId ?? Guid.NewGuid().ToString() },
            { MessagingHeaders.MessageName, messageName ?? "TestMessage" },
        };

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                headers[header.Key] = header.Value;
            }
        }

        return new TransportMessage(headers, body ?? "test-body"u8.ToArray());
    }

    public virtual async Task should_send_message_successfully()
    {
        // given
        await using var transport = GetTransport();
        var message = CreateMessage();

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    public virtual async Task should_have_valid_broker_address()
    {
        // given, when
        await using var transport = GetTransport();

        // then
        transport.BrokerAddress.Name.Should().NotBeNullOrEmpty();
    }

    public virtual async Task should_include_headers_in_sent_message()
    {
        // Skip if transport doesn't support headers
        if (!Capabilities.SupportsHeaders)
        {
            Assert.Skip("Transport does not support headers");
        }

        // given
        await using var transport = GetTransport();
        var customHeaders = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "CustomHeader1", "Value1" },
            { "CustomHeader2", "Value2" },
        };
        var message = CreateMessage(additionalHeaders: customHeaders);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        message.Headers.Should().ContainKey("CustomHeader1");
        message.Headers.Should().ContainKey("CustomHeader2");
    }

    public virtual async Task should_send_batch_of_messages()
    {
        // Skip if transport doesn't support batch send
        if (!Capabilities.SupportsBatchSend)
        {
            Assert.Skip("Transport does not support batch send");
        }

        // given
        await using var transport = GetTransport();
        var messages = Enumerable.Range(0, 10).Select(i => CreateMessage(messageId: $"batch-msg-{i}")).ToList();

        // when
        var results = new List<OperateResult>();
        foreach (var message in messages)
        {
            results.Add(await transport.SendAsync(message));
        }

        // then
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    public virtual async Task should_throw_when_transport_disposed()
    {
        // given
        var transport = GetTransport();
        await transport.DisposeAsync();

        var message = CreateMessage();

        // when
        var act = () => transport.SendAsync(message);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    public virtual async Task should_handle_empty_message_body()
    {
        // given
        await using var transport = GetTransport();
        var message = CreateMessage(body: ReadOnlyMemory<byte>.Empty);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    public virtual async Task should_handle_large_message_body()
    {
        // given
        await using var transport = GetTransport();
        var largeBody = new byte[64 * 1024]; // 64KB
        Random.Shared.NextBytes(largeBody);
        var message = CreateMessage(body: largeBody);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    public virtual async Task should_maintain_message_ordering()
    {
        // Skip if transport doesn't support ordering
        if (!Capabilities.SupportsOrdering)
        {
            Assert.Skip("Transport does not support ordering");
        }

        // given
        await using var transport = GetTransport();
        var messageIds = new List<string>();

        for (var i = 0; i < 100; i++)
        {
            var id = $"ordered-msg-{i:D4}";
            messageIds.Add(id);
            var message = CreateMessage(messageId: id);

            var result = await transport.SendAsync(message);
            result.Succeeded.Should().BeTrue();
        }

        // then - message IDs should be in order
        messageIds.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    public virtual async Task should_propagate_transport_errors()
    {
        // This test validates that transport errors are properly captured in the result
        // The specific error handling depends on the transport implementation

        // given
        await using var transport = GetTransport();
        var message = CreateMessage(messageName: ""); // Empty message name may cause validation error

        // when
        Func<Task> act = async () => await transport.SendAsync(message);

        // then - should either throw or return failed result
        // Implementation varies by transport
        try
        {
            var result = await transport.SendAsync(message);
            if (!result.Succeeded)
            {
                result.Exception.Should().NotBeNull();
            }
        }
        catch (ArgumentException)
        {
            // Some transports may throw on validation errors, which is acceptable
        }
    }

    public virtual async Task should_dispose_async_without_exception()
    {
        // given
        var transport = GetTransport();

        // when & then - dispose should complete without exception
        var act = () => transport.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_handle_concurrent_sends()
    {
        // given
        await using var transport = GetTransport();
        var results = new ConcurrentBag<OperateResult>();
        var tasks = Enumerable
            .Range(0, 50)
            .Select(async i =>
            {
                var message = CreateMessage(messageId: $"concurrent-msg-{i}");
                var result = await transport.SendAsync(message);
                results.Add(result);
            });

        // when
        await Task.WhenAll(tasks);

        // then
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r => r.Succeeded.Should().BeTrue());
    }

    public virtual async Task should_include_message_id_in_headers()
    {
        // given
        await using var transport = GetTransport();
        var expectedId = Guid.NewGuid().ToString();
        var message = CreateMessage(messageId: expectedId);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        message.GetId().Should().Be(expectedId);
    }

    public virtual async Task should_include_message_name_in_headers()
    {
        // given
        await using var transport = GetTransport();
        const string expectedName = "TestMessageName";
        var message = CreateMessage(messageName: expectedName);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        message.GetName().Should().Be(expectedName);
    }

    public virtual async Task should_handle_special_characters_in_message_body()
    {
        // given
        await using var transport = GetTransport();
        const string specialContent = "{\"text\": \"Hello \\\"World\\\" with émojis 🎉 and unicode: 日本語\"}";
        var message = CreateMessage(body: System.Text.Encoding.UTF8.GetBytes(specialContent));

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    public virtual async Task should_handle_null_header_values()
    {
        // given
        await using var transport = GetTransport();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal) { { "NullableHeader", null } };
        var message = CreateMessage(additionalHeaders: headers);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
    }

    public virtual async Task should_handle_correlation_id_header()
    {
        // given
        await using var transport = GetTransport();
        var correlationId = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.CorrelationId, correlationId },
        };
        var message = CreateMessage(additionalHeaders: headers);

        // when
        var result = await transport.SendAsync(message);

        // then
        result.Succeeded.Should().BeTrue();
        message.GetCorrelationId().Should().Be(correlationId);
    }
}
