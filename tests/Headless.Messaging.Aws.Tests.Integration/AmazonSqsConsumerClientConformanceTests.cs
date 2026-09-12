// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Tests.Capabilities;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsConsumerClientConformanceTests(LocalStackTestFixture fixture)
    : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => "AWS/LocalStack";

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseAws(options =>
        {
            options.Region = Amazon.RegionEndpoint.USEast1;
            options.SnsServiceUrl = fixture.ConnectionString;
            options.SqsServiceUrl = fixture.ConnectionString;
            options.Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");
        });
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public async Task should_round_trip_unkeyed_queue_headers_beyond_the_native_attribute_limit()
    {
        await using var session = await fixture.CreateConformanceSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("N"),
            [Headers.MessageName] = session.Destination,
            [Headers.Intent] = nameof(MessageLane.Queue),
            ["optional-metadata"] = null,
            ["headless-aws-headers-custom"] = "application-value",
        };
        for (var index = 0; index < 12; index++)
        {
            headers[$"business-{index}"] = $"value-{index}";
        }

        var body = "unkeyed-queue-body"u8.ToArray();
        var result = await session.PublishAsync(new TransportMessage(headers, body), AbortToken);
        result.Succeeded.Should().BeTrue();

        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        delivery.Message.Body.ToArray().Should().Equal(body);
        foreach (var header in headers)
        {
            delivery.Message.Headers.Should().Contain(header.Key, header.Value);
        }

        delivery.Message.Headers.Should().NotContainKey(Headers.RoutingAffinityKey);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);
    }

    [Fact]
    public override Task should_match_production_runtime_capabilities()
    {
        return base.should_match_production_runtime_capabilities();
    }

    [Fact]
    public async Task should_fan_out_bus_message_to_distinct_real_subscriptions()
    {
        RequireSupport(TransportConformanceScenario.BusRoundTrip);
        var destination = $"bus-{Guid.NewGuid():N}";
        await using var first = await fixture.CreateBusSessionAsync(
            destination,
            $"group-{Guid.NewGuid():N}",
            AbortToken
        );
        await using var second = await fixture.CreateBusSessionAsync(
            destination,
            $"group-{Guid.NewGuid():N}",
            AbortToken
        );

        await TransportBusConformance.AssertFanOutAsync(first, second, AbortToken);
    }

    [Fact]
    public Task should_deliver_one_bus_copy_per_group_while_replicas_compete()
    {
        var driver = new AwsProviderConformanceDriver(fixture);
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(driver, AbortToken);
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        var driver = new AwsProviderConformanceDriver(fixture);
        return TransportProviderConformance.AssertQueueOwnershipAsync(driver, AbortToken);
    }

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue()
    {
        var driver = new AwsProviderConformanceDriver(fixture);
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(driver, AbortToken);
    }

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery()
    {
        return base.should_commit_real_delivery_and_prevent_redelivery();
    }

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery()
    {
        return base.should_reject_real_delivery_and_observe_redelivery();
    }

    [Fact]
    public override Task should_isolate_unique_destinations()
    {
        return base.should_isolate_unique_destinations();
    }

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound()
    {
        return base.should_shutdown_idle_consumer_within_bound();
    }

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active()
    {
        return base.should_bound_shutdown_while_handler_is_active();
    }
}
