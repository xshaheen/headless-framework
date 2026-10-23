// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

/// <summary>
/// The shared consumer suite on the Azure Service Bus emulator. Each session reads a queue <c>Config.json</c> declares,
/// with provisioning off, so only scenarios that need no entity created at run time run here.
/// </summary>
[Collection("AzureServiceBusEmulator")]
public sealed class AzureServiceBusEmulatorConsumerClientHarnessTests(AzureServiceBusEmulatorFixture fixture)
    : TransportConsumerConformanceTestsBase
{
    protected override string ProviderName => AzureServiceBusEmulatorFixture.ProviderName;

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        var connectionString = fixture.ConnectionString;
        setup.UseAzureServiceBus(options =>
        {
            options.ConnectionString = connectionString;
            options.AutoProvision = false;
        });
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(options, cancellationToken);
    }

    // Service Bus moves a message that spends its max delivery count to the queue's dead-letter subqueue.
    protected override bool HasDeadLetterDestination => true;

    protected override ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        TransportConsumerConformanceSession session,
        CancellationToken cancellationToken
    )
    {
        return fixture.TakeDeadLetteredMessageIdsAsync(session, cancellationToken);
    }

    [Fact]
    public override Task should_match_production_runtime_capabilities()
    {
        return base.should_match_production_runtime_capabilities();
    }

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers()
    {
        return base.should_round_trip_queue_message_body_and_headers();
    }

    [Fact]
    public override Task should_dispatch_empty_message_body()
    {
        return base.should_dispatch_empty_message_body();
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
    public override Task should_not_settle_another_delivery_when_a_settled_value_is_used_again()
    {
        return base.should_not_settle_another_delivery_when_a_settled_value_is_used_again();
    }

    [Fact]
    public override Task should_keep_the_delivery_and_the_listener_when_the_callback_throws()
    {
        return base.should_keep_the_delivery_and_the_listener_when_the_callback_throws();
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
