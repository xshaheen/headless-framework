// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class BrokerAddressDisplayTests
{
    [Fact]
    public void format_should_preserve_plain_endpoint_values()
    {
        BrokerAddressDisplay.Format("broker:9092").Should().Be("broker:9092");
    }

    [Fact]
    public void format_should_strip_credentials_from_absolute_uri()
    {
        BrokerAddressDisplay.Format("pulsar://user:secret@broker:6650").Should().Be("pulsar://broker:6650");
    }

    [Fact]
    public void format_should_strip_credentials_from_scheme_less_endpoint_when_credentials_are_embedded()
    {
        BrokerAddressDisplay.Format("user:secret@broker:9092").Should().Be("broker:9092");
    }

    [Fact]
    public void format_many_should_strip_credentials_from_each_entry()
    {
        BrokerAddressDisplay
            .FormatMany("nats://user:secret@localhost:4222, nats://admin:secret@example.com:4223")
            .Should()
            .Be("nats://localhost:4222,nats://example.com:4223");
    }

    [Fact]
    public void format_should_strip_query_string_and_fragment_secrets()
    {
        BrokerAddressDisplay
            .Format("nats://user:secret@broker:4222/orders?token=secret#jwt=secret")
            .Should()
            .Be("nats://broker:4222/orders");
    }

    [Fact]
    public void format_should_format_dns_endpoints()
    {
        BrokerAddressDisplay.Format(new DnsEndPoint("redis.example.com", 6380)).Should().Be("redis.example.com:6380");
    }
}
