// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Capabilities;

namespace Tests;

public sealed class InMemoryProviderConformanceTests
{
    [Fact]
    public Task should_deliver_one_bus_copy_per_group_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusSubscriberGroupsAsync(
            _CreateDriver(),
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public Task should_deliver_one_owned_queue_copy_across_replicas()
    {
        return TransportProviderConformance.AssertQueueOwnershipAsync(
            _CreateDriver(),
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public Task should_isolate_same_logical_name_between_bus_and_queue()
    {
        return TransportProviderConformance.AssertSameNameLaneIsolationAsync(
            _CreateDriver(),
            TestContext.Current.CancellationToken
        );
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
