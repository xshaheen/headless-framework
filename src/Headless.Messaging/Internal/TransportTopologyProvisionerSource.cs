// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>
/// Offers each registered <see cref="ITransportTopologyProvisioner"/> as one resource provisioner, over the topology the
/// registered consumers and messages describe. A named transport's provisioners get only the subscriptions bound to it.
/// </summary>
internal sealed class TransportTopologyProvisionerSource(
    IEnumerable<ITransportTopologyProvisioner> provisioners,
    IServiceProvider services,
    IMessageMetadataRegistry? messages = null
) : IResourceProvisionerSource
{
    // Built once per lookup and shared by every provisioner of the run: it reads only registrations frozen with the
    // container, and runtime subscriptions are never in it, so it cannot change while a run lasts. The consumer
    // selector is resolved only when an operation needs the topology: it builds the consumer registry and the transport,
    // and the host start lists every provisioner just to read its StartupCheck, before messaging bootstraps.
    public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken)
    {
        var topology = new Lazy<MessagingTopology>(() =>
            BuildTopology(
                services.GetRequiredService<MethodMatcherCache>(),
                messages,
                services.GetService<IMessagingCapabilityModel>()
            )
        );
        List<IResourceProvisioner> resources =
        [
            .. provisioners.Select(p => new TransportTopologyResourceProvisioner(p, topology, transportName: null)),
        ];

        foreach (var named in services.GetService<MessagingTransports>()?.Named ?? [])
        {
            var namedTopology = new Lazy<MessagingTopology>(() =>
                BuildTopology(
                    services.GetRequiredService<MethodMatcherCache>(),
                    messages: null,
                    named.Capabilities,
                    named.Name
                )
            );

            resources.AddRange(
                named.Provisioners.Select(p => new TransportTopologyResourceProvisioner(p, namedTopology, named.Name))
            );
        }

        return Task.FromResult<IReadOnlyList<IResourceProvisioner>>(resources);
    }

    /// <summary>
    /// Builds the shared topology the registered consumers and messages need on one transport. It reads only
    /// registrations frozen with the container, so every caller of one host sees the same topology.
    /// </summary>
    /// <remarks>
    /// The default transport also gets the declared messages, whose Queue-lane queues its publishers declare. A named
    /// transport gets only the subscriptions bound to it: its consumers' topology is what this host knows it needs there.
    /// </remarks>
    internal static MessagingTopology BuildTopology(
        MethodMatcherCache selector,
        IMessageMetadataRegistry? messages,
        IMessagingCapabilityModel? capabilities = null,
        string? transportName = null
    )
    {
        var subscriptions = new List<TopologySubscription>();
        var transport = capabilities?.Providers.FirstOrDefault(static p => p.Role is MessagingProviderRole.Transport);

        foreach (var (key, descriptors) in selector.GetCandidatesBySubscription())
        {
            if (!string.Equals(key.TransportName, transportName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A runtime subscription carries a handler id: no deploy step knows it, so its client creates its topology.
            var names = descriptors
                .Where(static d => d.HandlerId is null)
                .Select(static d => d.MessageName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (names.Count > 0)
            {
                // Whether a lock is configured plays no part here: only the broker's own single active consumer is a
                // setting of the subscription's broker objects.
                var intake = SequentialIntakeResolver.Resolve(
                    key,
                    descriptors,
                    transport,
                    distributedLockConfigured: false
                );

                subscriptions.Add(
                    new TopologySubscription(key.SubscriptionName, key.Lane, key.Kind, names)
                    {
                        SingleActiveConsumer = intake is SequentialIntake.BrokerSingleActiveConsumer,
                    }
                );
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
    Lazy<MessagingTopology> topology,
    string? transportName
) : IResourceProvisioner
{
    public string Kind => provisioner.Kind;

    // Two transports of one provider each own a topology resource, so a named one carries its transport's name.
    public string Name => transportName is null ? "topology" : $"topology-{transportName}";

    public ResourceStartupCheck StartupCheck => provisioner.StartupCheck;

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
