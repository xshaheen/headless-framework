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
    public void should_reject_flat_attributes_without_the_exact_header_bag(string headerName)
    {
        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = new() { DataType = "String", StringValue = "message-1" },
            [Headers.MessageName] = new() { DataType = "String", StringValue = "orders" },
            [headerName] = new() { DataType = "String", StringValue = "not a JSON bag" },
        };

        var decode = () => SqsHeaderCodec.Decode(attributes);

        decode.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_decode_an_unkeyed_header_bag_without_inventing_affinity()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "orders",
            ["optional-metadata"] = null,
            ["headless-aws-headers-custom"] = "application-value",
            ["headless-aws-headers-v2"] = "also-an-application-value",
        };
        var attributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        {
            [SqsHeaderCodec.AttributeName] = new()
            {
                DataType = "String",
                StringValue = JsonSerializer.Serialize(headers),
            },
        };

        var restored = SqsHeaderCodec.Decode(attributes);

        restored.Should().BeEquivalentTo(headers);
        restored.Should().NotContainKey(Headers.RoutingAffinityKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("order-42")]
    public void should_pack_every_message_losslessly_into_one_header_bag(string? key)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = key is null ? "orders" : "orders.fifo",
            ["headless-aws-headers-custom"] = "custom-value",
            ["headless-aws-headers-v2"] = "version-like-application-value",
            ["optional-metadata"] = null,
        };
        for (var index = 0; index < 10; index++)
        {
            headers.Add($"business-{index}", $"value-{index}");
        }
        if (key is not null)
        {
            headers[Headers.RoutingAffinityKey] = key;
        }

        var attributes = SqsHeaderCodec.Encode(new TransportMessage(headers, "payload"u8.ToArray()));

        attributes.Should().ContainSingle();
        attributes.Should().ContainKey(SqsHeaderCodec.AttributeName);
        attributes[SqsHeaderCodec.AttributeName].DataType.Should().Be("String");
        SqsHeaderCodec.Decode(attributes).Should().BeEquivalentTo(headers);
    }
}
