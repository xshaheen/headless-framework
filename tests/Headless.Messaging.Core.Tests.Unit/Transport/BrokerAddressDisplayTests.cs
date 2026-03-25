// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class BrokerAddressDisplayTests
{
    [Fact]
    public void get_display_endpoint_should_preserve_plain_endpoint_values()
    {
        BrokerAddressDisplay.GetDisplayEndpoint("broker:9092").Should().Be("broker:9092");
    }

    [Fact]
    public void get_display_endpoint_should_strip_credentials_from_absolute_uri()
    {
        BrokerAddressDisplay.GetDisplayEndpoint("pulsar://user:secret@broker:6650").Should().Be("pulsar://broker:6650");
    }

    [Fact]
    public void get_display_endpoint_should_strip_credentials_from_scheme_less_endpoint_when_inferred_scheme_is_provided()
    {
        BrokerAddressDisplay
            .GetDisplayEndpoint("user:secret@broker:9092", inferredScheme: "kafka")
            .Should()
            .Be("broker:9092");
    }

    [Fact]
    public void get_display_endpoints_should_strip_credentials_from_each_entry()
    {
        BrokerAddressDisplay
            .GetDisplayEndpoints(
                "nats://user:secret@localhost:4222, nats://admin:secret@example.com:4223",
                inferredScheme: "nats"
            )
            .Should()
            .Be("nats://localhost:4222,nats://example.com:4223");
    }

    [Fact]
    public void get_display_endpoint_should_format_dns_endpoints()
    {
        BrokerAddressDisplay
            .GetDisplayEndpoint(new DnsEndPoint("redis.example.com", 6380))
            .Should()
            .Be("redis.example.com:6380");
    }
}
