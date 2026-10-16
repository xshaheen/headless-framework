// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;

namespace Tests;

public sealed class AzureServiceBusMessageBuilderExtensionsTests
{
    [Fact]
    public void should_store_partition_key_header_contribution()
    {
        var builder = new MessageContractBuilder<TestMessage>("tests.asb.message", "v1");

        builder.OnBus(bus => bus.UseAzureServiceBus(asb => asb.PartitionKey(static message => message.TenantId)));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().Bus.ProviderConfigs.Values.Single()
        ).HeaderContributions.Single();

        contribution.HeaderName.Should().Be(AzureServiceBusMessagingHeaders.PartitionKey);
        contribution.Selector(new TestMessage("tenant-a")).Should().Be("tenant-a");
    }

    [Fact]
    public void should_reject_partition_key_longer_than_service_bus_limit()
    {
        var builder = new MessageContractBuilder<TestMessage>("tests.asb.message", "v1");

        builder.OnBus(bus => bus.UseAzureServiceBus(asb => asb.PartitionKey(static _ => new string('x', 129))));
        var contribution = (
            (IProviderHeaderContributions)builder.Build().Bus.ProviderConfigs.Values.Single()
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

    [Fact]
    public void should_set_time_to_live_to_remaining_time_when_message_carries_delivery_deadline()
    {
        // given
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var message = _TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.DeliverBy] = now.AddSeconds(90).ToString("O", CultureInfo.InvariantCulture),
            }
        );

        // when
        var serviceBusMessage = AzureServiceBusMessageBuilder.Build(message, enableSessions: false, now);

        // then
        serviceBusMessage.TimeToLive.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-date")]
    [InlineData("2026-10-15T11:59:00.0000000+00:00")]
    public void should_leave_time_to_live_unset_when_deadline_is_absent_unreadable_or_passed(string? deliverBy)
    {
        // given
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var extra = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (deliverBy is not null)
        {
            extra[Headers.DeliverBy] = deliverBy;
        }

        var baseline = new Azure.Messaging.ServiceBus.ServiceBusMessage().TimeToLive;

        // when
        var serviceBusMessage = AzureServiceBusMessageBuilder.Build(
            _TransportMessage(extra),
            enableSessions: false,
            now
        );

        // then — the entity default applies
        serviceBusMessage.TimeToLive.Should().Be(baseline);
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
