// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

/// <summary>
/// The shared broker-observed delivery and settlement suite against the in-process broker, each session on a queue of
/// its own.
/// </summary>
public sealed class InMemoryConsumerConformanceTests : TransportConsumerConformanceTestsBase
{
    private readonly InMemoryProviderConformanceTests.InMemoryProviderConformanceDriver _driver =
        InMemoryProviderConformanceTests.CreateDriver();

    protected override string ProviderName => _driver.ProviderName;

    protected override void ConfigureTransport(MessagingSetupBuilder setup) => setup.UseInMemory();

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    ) => CreateSessionAsync(new TransportConformanceSessionOptions(), cancellationToken);

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        // InMemory has no header builder, delivery budget, dead-letter destination, time-to-live, or key ordering, so
        // its cells for those are not applicable and only the concurrency is configurable.
        if (options with { Concurrency = 1 } != new TransportConformanceSessionOptions())
        {
            throw new NotSupportedException($"The in-memory transport does not configure {options}.");
        }

        var destination = $"conformance-{Guid.NewGuid():N}";

        return _driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, destination, destination, "replica-1"),
            options.Concurrency,
            cancellationToken
        );
    }

    [Fact]
    public override Task should_match_production_runtime_capabilities() =>
        base.should_match_production_runtime_capabilities();

    [Fact]
    public override Task should_round_trip_queue_message_body_and_headers() =>
        base.should_round_trip_queue_message_body_and_headers();

    [Fact]
    public override Task should_dispatch_empty_message_body() => base.should_dispatch_empty_message_body();

    [Fact]
    public override Task should_commit_real_delivery_and_prevent_redelivery() =>
        base.should_commit_real_delivery_and_prevent_redelivery();

    [Fact]
    public override Task should_reject_real_delivery_and_observe_redelivery() =>
        base.should_reject_real_delivery_and_observe_redelivery();

    [Fact]
    public override Task should_not_settle_another_delivery_when_a_settled_value_is_used_again() =>
        base.should_not_settle_another_delivery_when_a_settled_value_is_used_again();

    [Fact]
    public override Task should_keep_the_delivery_and_the_listener_when_the_callback_throws() =>
        base.should_keep_the_delivery_and_the_listener_when_the_callback_throws();

    [Fact]
    public override Task should_isolate_unique_destinations() => base.should_isolate_unique_destinations();

    [Fact]
    public override Task should_shutdown_idle_consumer_within_bound() =>
        base.should_shutdown_idle_consumer_within_bound();

    [Fact]
    public override Task should_bound_shutdown_while_handler_is_active() =>
        base.should_bound_shutdown_while_handler_is_active();
}
