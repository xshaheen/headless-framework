// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

/// <summary>
/// Offers each registered <see cref="ITransportTopologyProvisioner"/> as one stateful resource, over the topology the
/// registered consumers and messages describe.
/// </summary>
internal sealed class TransportTopologyResourceSource(
    IEnumerable<ITransportTopologyProvisioner> provisioners,
    MethodMatcherCache selector,
    IMessageMetadataRegistry? messages = null
) : IStatefulResourceSource
{
    // Built once per lookup and shared by every resource of the run: it reads only registrations frozen with the
    // container, and runtime subscriptions are never in it, so it cannot change while a run lasts.
    public Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(CancellationToken cancellationToken)
    {
        var topology = new Lazy<MessagingTopology>(() => BuildTopology(selector, messages));
        IReadOnlyList<IStatefulResource> resources =
        [
            .. provisioners.Select(p => new TransportTopologyResource(p, topology)),
        ];

        return Task.FromResult(resources);
    }

    /// <summary>
    /// Builds the shared topology the registered consumers and messages need. It reads only registrations frozen with
    /// the container, so every caller of one host sees the same topology.
    /// </summary>
    internal static MessagingTopology BuildTopology(MethodMatcherCache selector, IMessageMetadataRegistry? messages)
    {
        var subscriptions = new List<TopologySubscription>();

        foreach (var (key, descriptors) in selector.GetCandidatesBySubscription())
        {
            // A runtime subscription carries a handler id: no deploy step knows it, so its client creates its topology.
            var names = descriptors
                .Where(static d => d.HandlerId is null)
                .Select(static d => d.MessageName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (names.Count > 0)
            {
                subscriptions.Add(new TopologySubscription(key.SubscriptionName, key.Lane, key.Kind, names));
            }
        }

        List<TopologyMessage> registered =
        [
            .. (messages?.GetAll() ?? [])
                .Select(static m => new TopologyMessage(m.Route.MessageName, m.Route.Lane))
                .Distinct()
                .OrderBy(static m => m.MessageName, StringComparer.Ordinal)
                .ThenBy(static m => m.Lane),
        ];

        return new MessagingTopology(
            [
                .. subscriptions
                    .OrderBy(static s => s.SubscriptionName, StringComparer.Ordinal)
                    .ThenBy(static s => s.Lane)
                    .ThenBy(static s => s.Kind),
            ],
            registered
        );
    }
}

/// <summary>One transport's shared topology as a stateful resource.</summary>
internal sealed class TransportTopologyResource(
    ITransportTopologyProvisioner provisioner,
    Lazy<MessagingTopology> topology
) : IStatefulResource
{
    public string Type => provisioner.Broker;

    public string Name => "topology";

    public Task SetupAsync(CancellationToken cancellationToken) =>
        provisioner.SetupAsync(topology.Value, cancellationToken);

    public Task CheckAsync(CancellationToken cancellationToken) =>
        provisioner.CheckAsync(topology.Value, cancellationToken);

    public Task ClearStateAsync(CancellationToken cancellationToken) =>
        provisioner.ClearAsync(topology.Value, cancellationToken);

    public Task TeardownAsync(CancellationToken cancellationToken) =>
        provisioner.TeardownAsync(topology.Value, cancellationToken);

    public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
        provisioner.GetStatusAsync(topology.Value, cancellationToken);
}
