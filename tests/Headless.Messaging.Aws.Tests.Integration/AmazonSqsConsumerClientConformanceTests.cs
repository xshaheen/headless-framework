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
