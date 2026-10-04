// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Capabilities;
using Tests.RequestReply;

namespace Tests;

public sealed class InMemoryProviderConformanceTests : TransportRequestReplyConformanceTestsBase
{
    protected override ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        return ValueTask.FromResult<TransportProviderConformanceDriver>(_CreateDriver());
    }

    [Fact]
    public Task should_prove_routing_affinity_mapping_or_rejection() =>
        TransportRoutingAffinityConformance.AssertAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_deliver_one_bus_copy_per_consumer_identity_while_replicas_compete()
    {
        return TransportProviderConformance.AssertBusConsumerIdentitiesAsync(_CreateDriver(), AbortToken);
    }

    [Fact]
    public Task should_deliver_every_bus_message_to_every_every_instance_replica()
    {
        return TransportProviderConformance.AssertBusEveryInstanceAsync(_CreateDriver(), AbortToken);
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

    [Fact]
    public Task should_reject_requests_and_responders_at_startup_on_a_transport_without_request_reply() =>
        TransportRequestReplyConformance.AssertRejectedAtStartupAsync(
            new InMemoryWithoutRequestReplyDriver(new MemoryQueue(NullLogger<MemoryQueue>.Instance)),
            AbortToken
        );

    private static InMemoryProviderConformanceDriver _CreateDriver()
    {
        return new InMemoryProviderConformanceDriver(new MemoryQueue(NullLogger<MemoryQueue>.Instance));
    }

    /// <summary>
    /// The same in-process broker with request/reply left undeclared, standing for a provider that rejects it, so the
    /// shared startup-rejection proof runs here too.
    /// </summary>
    private sealed class InMemoryWithoutRequestReplyDriver(MemoryQueue queue) : InMemoryProviderConformanceDriver(queue)
    {
        public override bool SupportsRequestReply => false;

        public override void ConfigureRequestReplyServices(IServiceCollection services)
        {
            base.ConfigureRequestReplyServices(services);

            var declared = services
                .Where(descriptor =>
                    descriptor.ImplementationInstance
                        is MessagingProviderCapabilities { Role: MessagingProviderRole.Transport }
                )
                .ToList();

            foreach (var descriptor in declared)
            {
                services.Remove(descriptor);
            }

            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    "InMemoryWithoutRequestReply",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true
                )
            );
        }
    }

    private class InMemoryProviderConformanceDriver(MemoryQueue queue) : TransportProviderConformanceDriver
    {
        private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
            "InMemory"
        ];

        public override string ProviderName => _Profile.Provider;

        public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

        public override bool SupportsEveryInstance => true;

        public override bool SupportsRequestReply => true;

        public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup)
        {
            setup.UseInMemory();
        }

        public override void ConfigureRequestReplyServices(IServiceCollection services)
        {
            // Registered after messaging's own MemoryQueue, so every host resolves the one broker the scenario shares.
            services.AddSingleton(queue);
        }

        public override ValueTask<bool> HasReplyObjectsAsync(string replyAddress, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(queue.HasReplyListener(replyAddress));
        }

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
#pragma warning restore CA2000
            var consumer = await new InMemoryConsumerClientFactory(queue).CreateAsync(
                endpoint.ToRequest(),
                cancellationToken
            );
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
