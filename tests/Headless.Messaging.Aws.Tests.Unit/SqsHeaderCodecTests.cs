// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqsHeaderCodecTests : TestBase
{
    [Theory]
    [InlineData("headless-aws-headers-custom")]
    [InlineData("headless-aws-headers-v2")]
    public void should_decode_legacy_prefix_headers_without_interpreting_their_value(string headerName)
    {
        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = new() { DataType = "String", StringValue = "message-1" },
            [Headers.MessageName] = new() { DataType = "String", StringValue = "orders.fifo" },
            [headerName] = new() { DataType = "String", StringValue = "not a JSON bag" },
        };

        var headers = SqsHeaderCodec.Decode(attributes);

        headers.Should().HaveCount(3);
        headers[Headers.MessageId].Should().Be("message-1");
        headers[Headers.MessageName].Should().Be("orders.fifo");
        headers[headerName].Should().Be("not a JSON bag");
        headers.Should().NotContainKey(Headers.RoutingAffinityKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public void should_preserve_application_prefix_headers_on_keyed_and_unkeyed_sends(string? key)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "orders.fifo",
            ["headless-aws-headers-custom"] = "custom-value",
            ["headless-aws-headers-v2"] = "version-like-application-value",
        };
        if (key is not null)
        {
            headers[Headers.RoutingAffinityKey] = key;
        }

        var attributes = SqsHeaderCodec.Encode(new TransportMessage(headers, "payload"u8.ToArray()));
        var restored = SqsHeaderCodec.Decode(attributes);

        restored.Should().BeEquivalentTo(headers);
        attributes.ContainsKey(SqsHeaderCodec.AttributeName).Should().Be(key is not null);
    }
}
