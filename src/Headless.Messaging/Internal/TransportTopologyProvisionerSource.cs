// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

/// <summary>
/// Offers each registered <see cref="ITransportTopologyProvisioner"/> as one resource provisioner, over the topology the
/// registered consumers and messages describe.
/// </summary>
internal sealed class TransportTopologyProvisionerSource(
    IEnumerable<ITransportTopologyProvisioner> provisioners,
    MethodMatcherCache selector,
    IMessageMetadataRegistry? messages = null
) : IResourceProvisionerSource
{
    // Built once per lookup and shared by every provisioner of the run: it reads only registrations frozen with the
    // container, and runtime subscriptions are never in it, so it cannot change while a run lasts.
    public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken)
    {
        var topology = new Lazy<MessagingTopology>(() => BuildTopology(selector, messages));
        IReadOnlyList<IResourceProvisioner> resources =
        [
            .. provisioners.Select(p => new TransportTopologyResourceProvisioner(p, topology)),
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

/// <summary>One transport's shared topology as a resource provisioner.</summary>
internal sealed class TransportTopologyResourceProvisioner(
    ITransportTopologyProvisioner provisioner,
    Lazy<MessagingTopology> topology
) : IResourceProvisioner
{
    public string Kind => provisioner.Kind;

    public string Name => "topology";

    public bool CanPurge => provisioner.CanPurge;

    public bool CanDelete => provisioner.CanDelete;

    public Task ProvisionAsync(CancellationToken cancellationToken) =>
        provisioner.ProvisionAsync(topology.Value, cancellationToken);

    public Task VerifyAsync(CancellationToken cancellationToken) =>
        provisioner.VerifyAsync(topology.Value, cancellationToken);

    public Task PurgeAsync(CancellationToken cancellationToken) =>
        provisioner.PurgeAsync(topology.Value, cancellationToken);

    public Task DeleteAsync(CancellationToken cancellationToken) =>
        provisioner.DeleteAsync(topology.Value, cancellationToken);

    public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
        provisioner.GetStatusAsync(topology.Value, cancellationToken);
}
