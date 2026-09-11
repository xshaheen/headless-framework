// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Capabilities;

namespace Tests;

public sealed class InMemoryProviderConformanceTests : TestBase
{
    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_deliver_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(_CreateDriver(), AbortToken);
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(_CreateDriver(), AbortToken);
    }

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(_CreateDriver(), AbortToken);
    }

    private static InMemoryProviderConformanceDriver _CreateDriver()
    {
        return new InMemoryProviderConformanceDriver(new MemoryQueue(NullLogger<MemoryQueue>.Instance));
    }

    private sealed class InMemoryProviderConformanceDriver(MemoryQueue queue) : TransportProviderConformanceDriver
    {
        private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
            "InMemory"
        ];

        public override string ProviderName => _Profile.Provider;

        public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

        public override async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
            TransportConformanceEndpoint endpoint,
            CancellationToken cancellationToken
        )
        {
#pragma warning disable CA2000 // Ownership transfers to the returned conformance session.
            ITransport producer = endpoint.Lane switch
            {
                MessageLane.Bus => new InMemoryBusTransport(queue, NullLogger<InMemoryBusTransport>.Instance),
                MessageLane.Queue => new InMemoryQueueTransport(queue, NullLogger<InMemoryQueueTransport>.Instance),
                _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Lane, null),
            };
            var consumer = new InMemoryConsumerClient(queue, endpoint.SubscriberGroup, 1, endpoint.Lane);
#pragma warning restore CA2000
            await consumer.SubscribeAsync([endpoint.LogicalName], cancellationToken);

            return new TransportConsumerConformanceSession(
                endpoint.LogicalName,
                producer,
                consumer,
                TimeSpan.FromSeconds(1)
            );
        }
    }
}
