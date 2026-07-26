// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AzureServiceBusMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_partition_key_header_contribution()
    {
        var builder = new BusMessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseAzureServiceBus(asb => asb.PartitionKey(static message => message.TenantId));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(AzureServiceBusMessagingHeaders.PartitionKey);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Fact]
    public void should_reject_partition_key_longer_than_service_bus_limit()
    {
        var builder = new BusMessageBuilder<TestMessage>(new ServiceCollection());

        builder.UseAzureServiceBus(asb => asb.PartitionKey(static _ => new string('x', 129)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        var act = () => contribution.Selector(new TestMessage("tenant-a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*PartitionKey*128*");
    }

    [Fact]
    public void should_map_partition_key_header_to_service_bus_message()
    {
        var message = _TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [AzureServiceBusMessagingHeaders.PartitionKey] = "tenant-a",
            }
        );

        var serviceBusMessage = AzureServiceBusMessageBuilder.Build(message, enableSessions: false);

        serviceBusMessage.PartitionKey.Should().Be("tenant-a");
    }

    [Fact]
    public void should_reject_partition_key_that_differs_from_session_id_when_sessions_are_enabled()
    {
        var message = _TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [AzureServiceBusMessagingHeaders.SessionId] = "session-a",
                [AzureServiceBusMessagingHeaders.PartitionKey] = "partition-b",
            }
        );

        var act = () => AzureServiceBusMessageBuilder.Build(message, enableSessions: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*PartitionKey*SessionId*");
    }

    [Fact]
    public void should_use_partition_key_as_session_id_when_sessions_are_enabled_and_session_id_is_absent()
    {
        var message = _TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [AzureServiceBusMessagingHeaders.PartitionKey] = "tenant-a",
            }
        );

        var serviceBusMessage = AzureServiceBusMessageBuilder.Build(message, enableSessions: true);

        serviceBusMessage.SessionId.Should().Be("tenant-a");
        serviceBusMessage.PartitionKey.Should().Be("tenant-a");
    }

    private static TransportMessage _TransportMessage(Dictionary<string, string?> extraHeaders)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = "message-1",
            [Headers.MessageName] = "orders.created",
            [Headers.CorrelationId] = "message-1",
        };

        foreach (var pair in extraHeaders)
        {
            headers[pair.Key] = pair.Value;
        }

        return new TransportMessage(headers, "test"u8.ToArray());
    }

    private sealed record TestMessage(string TenantId);
}
