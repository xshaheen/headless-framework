// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging;
using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class BrokerAddressDisplayTests
{
    [Fact]
    public void should_preserve_plain_endpoint_values_when_format()
    {
        BrokerAddressDisplay.Format("broker:9092").Should().Be("broker:9092");
    }

    [Fact]
    public void should_strip_credentials_from_absolute_uri_when_format()
    {
        BrokerAddressDisplay.Format("pulsar://user:secret@broker:6650").Should().Be("pulsar://broker:6650");
    }

    [Fact]
    public void should_strip_credentials_from_scheme_less_endpoint_when_format_credentials_are_embedded()
    {
        BrokerAddressDisplay.Format("user:secret@broker:9092").Should().Be("broker:9092");
    }

    [Fact]
    public void should_strip_credentials_from_each_entry_when_format_many()
    {
        BrokerAddressDisplay
            .FormatMany("nats://user:secret@localhost:4222, nats://admin:secret@example.com:4223")
            .Should()
            .Be("nats://localhost:4222,nats://example.com:4223");
    }

    [Fact]
    public void should_strip_query_string_and_fragment_secrets_when_format()
    {
        BrokerAddressDisplay
            .Format("nats://user:secret@broker:4222/orders?token=secret#jwt=secret")
            .Should()
            .Be("nats://broker:4222/orders");
    }

    [Fact]
    public void should_format_dns_endpoints_when_format()
    {
        BrokerAddressDisplay.Format(new DnsEndPoint("redis.example.com", 6380)).Should().Be("redis.example.com:6380");
    }

    [Fact]
    public void should_preserve_endpoint_delimiters_on_round_trip_when_broker_address()
    {
        // given
        var address = new BrokerAddress("redis", "localhost:6379?password=a$b");

        // when
        var parsed = new BrokerAddress(address.ToString());

        // then
        parsed.Should().Be(address);
    }
}
