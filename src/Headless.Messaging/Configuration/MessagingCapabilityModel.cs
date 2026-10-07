// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging;

/// <summary>Composes immutable provider contributions into the runtime capability authority.</summary>
[PublicAPI]
public sealed class MessagingCapabilityModel : IMessageCapabilityGate
{
    private readonly FrozenDictionary<MessagingProviderRole, MessagingProviderCapabilities[]> _providersByRole;

    // The model is immutable once composed, so the role/lane union is resolved here instead of scanning
    // the provider arrays with a capturing predicate on every publish gate check.
    private readonly FrozenSet<(MessagingProviderRole Role, MessageLane Lane)> _supportedRoleLanes;

    private readonly FrozenDictionary<
        (MessageLane Lane, string MessageName),
        MessagingRoutingAffinityMapping
    > _affinityRoutes;

    private MessagingCapabilityModel(
        MessagingProviderCapabilities[] declaredCapabilities,
        MessagingProviderCapabilities[] providers
    )
    {
        _affinityRoutes = providers
            .Where(static provider => provider.Role == MessagingProviderRole.Transport)
            .SelectMany(static provider => provider.RoutingAffinityRoutes)
            .ToFrozenDictionary(static route => (route.Lane, route.MessageName), static route => route.Mapping);
        DeclaredCapabilities = Array.AsReadOnly(declaredCapabilities);
        Providers = Array.AsReadOnly(providers);
        _providersByRole = providers
            .GroupBy(static capability => capability.Role)
            .ToFrozenDictionary(static group => group.Key, static group => group.ToArray());
        _supportedRoleLanes = providers
            .SelectMany(static capability => capability.Lanes, static (capability, lane) => (capability.Role, lane))
            .ToFrozenSet();
    }

    /// <inheritdoc />
    public IReadOnlyList<MessagingProviderCapabilities> DeclaredCapabilities { get; }

    /// <inheritdoc />
    public IReadOnlyList<MessagingProviderCapabilities> Providers { get; }

    /// <inheritdoc />
    public bool IsFrozen => true;

    /// <inheritdoc />
    public InboxGuarantee? StorageInboxGuarantee => StorageProvider?.InboxGuarantee;

    /// <inheritdoc />
    public MessagingProviderCapabilities? StorageProvider =>
        _providersByRole.TryGetValue(MessagingProviderRole.Storage, out var storageProviders)
            ? storageProviders.Single()
            : null;

    /// <summary>Composes and freezes a deterministic capability model.</summary>
    public static MessagingCapabilityModel Compose(IEnumerable<MessagingProviderCapabilities> capabilities)
    {
        Argument.IsNotNull(capabilities);

        var declared = capabilities.ToArray();
        if (declared.Any(static capability => capability is null))
        {
            throw new ArgumentException("Capability contributions cannot contain null values.", nameof(capabilities));
        }

        var providers = new List<MessagingProviderCapabilities>();
        _ComposeTransport(declared, providers);
        _ComposeStorage(declared, providers);
        providers.AddRange(
            declared
                .Where(static capability => capability.Role == MessagingProviderRole.Coordination)
                .OrderBy(static capability => capability.Provider, StringComparer.Ordinal)
        );

        return new MessagingCapabilityModel(declared, [.. providers]);
    }

    /// <inheritdoc />
    public bool Supports(MessageLane lane, MessagingProviderRole role)
    {
        _EnsureDefinedLane(lane);
        return _supportedRoleLanes.Contains((role, lane));
    }

    /// <summary>Validates the frozen model against every registered semantic route.</summary>
    /// <param name="routes">Every registered semantic route.</param>
    /// <param name="hasDurableConsumers">Whether any durable consumer is registered.</param>
    /// <param name="minimumInboxGuarantee">The inbox guarantee the host requires from storage.</param>
    internal void ValidateStartup(
        IEnumerable<MessageRouteKey> routes,
        bool hasDurableConsumers = false,
        InboxGuarantee minimumInboxGuarantee = InboxGuarantee.Transactional
    )
    {
        Argument.IsNotNull(routes);
        Argument.IsInEnum(minimumInboxGuarantee);

        var routeArray = routes.ToArray();
        _RequireRole(MessagingProviderRole.Transport, "Messaging requires a transport provider contribution.");
        _RequireRole(MessagingProviderRole.Storage, "Messaging requires exactly one storage provider contribution.");

        if (hasDurableConsumers)
        {
            _EnsureInboxSupported(minimumInboxGuarantee);
        }

        foreach (var route in routeArray)
        {
            EnsureDirectSupported(route.Lane);

            if (!Supports(route.Lane, MessagingProviderRole.Storage))
            {
                throw new MessagingConfigurationException(
                    $"Storage provider does not support the {route.Lane} lane required by '{route.MessageName}'."
                );
            }
        }

        var transport = _providersByRole[MessagingProviderRole.Transport].Single();
        if (transport.SupportsIndependentLaneTopology)
        {
            return;
        }

        var collision = routeArray
            .GroupBy(static route => route.MessageName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(route => route.Lane).Distinct().Skip(1).Any());

        if (collision is not null)
        {
            var route = collision.First();
            throw new MessagingConfigurationException(
                $"Transport provider '{transport.Provider}' does not support independent Bus and Queue lane topology "
                    + $"for logical name '{route.MessageName}'."
            );
        }
    }

    private void _EnsureInboxSupported(InboxGuarantee minimumInboxGuarantee)
    {
        var storage = _providersByRole[MessagingProviderRole.Storage].Single();
        var available = storage.InboxGuarantee!.Value;

        var isSupported = minimumInboxGuarantee switch
        {
            InboxGuarantee.ProcessLocal => available
                is InboxGuarantee.ProcessLocal
                    or InboxGuarantee.Durable
                    or InboxGuarantee.Transactional,
            InboxGuarantee.Durable => available is InboxGuarantee.Durable or InboxGuarantee.Transactional,
            InboxGuarantee.Transactional => available is InboxGuarantee.Transactional,
            _ => throw new UnreachableException(),
        };

        if (isSupported)
        {
            return;
        }

        throw new MessagingConfigurationException(
            $"Durable consumers require the {minimumInboxGuarantee} inbox guarantee, but storage provider "
                + $"'{storage.Provider}' declares {available}. Select {nameof(InboxGuarantee.Durable)} "
                + "explicitly when durable duplicate suppression without atomic application-state coordination is acceptable, "
                + $"or select {nameof(InboxGuarantee.ProcessLocal)} explicitly for process-local development storage."
        );
    }

    internal void ValidateRoutingAffinityStartup(IEnumerable<MessageMetadata> routes)
    {
        foreach (var metadata in routes.Where(static route => route.RequiresRoutingAffinity))
        {
            if (!_affinityRoutes.ContainsKey((metadata.Route.Lane, metadata.Route.MessageName)))
            {
                throw new MessagingConfigurationException(
                    $"Routing affinity is required but unsupported for '{metadata.Route.MessageName}' ({metadata.Route.Lane})."
                );
            }
        }
    }

    internal void EnsureRoutingAffinitySupported(
        string messageName,
        MessageLane lane,
        string key,
        IDictionary<string, string?> headers
    )
    {
        if (!_affinityRoutes.TryGetValue((lane, messageName), out var mapping))
        {
            throw new MessagingConfigurationException(
                $"Routing affinity is unsupported or unverifiable for '{messageName}' ({lane})."
            );
        }

        mapping.Validate(key, headers);
    }

    /// <summary>Rejects a direct publish when the selected lane has no declared transport capability.</summary>
    internal void EnsureDirectSupported(MessageLane lane)
    {
        // Supports validates the lane itself; a second _EnsureDefinedLane here would only double the cost.
        if (Supports(lane, MessagingProviderRole.Transport))
        {
            return;
        }

        var transport =
            (
                _providersByRole.TryGetValue(MessagingProviderRole.Transport, out var transports)
                    ? transports.SingleOrDefault()
                    : null
            )
            ?? throw new MessagingConfigurationException(
                $"{lane} direct delivery is unsupported by the declared transport capabilities. "
                    + "Register the provider through AddMessagingProviderCapabilities; raw transport registrations are not capability evidence."
            );

        var supportedLanes = string.Join(", ", transport.Lanes.Order());

        throw new MessagingConfigurationException(
            $"{lane} direct delivery is unsupported: transport provider '{transport.Provider}' does not support {lane} delivery. "
                + $"Supported lanes: {supportedLanes}. "
                + $"Consume and publish this message on a supported lane, or select a transport that supports {lane}."
        );
    }

    /// <summary>
    /// Rejects an every-instance consumer, declared or attached at runtime, when the transport cannot give each process a
    /// Bus subscription of its own. Runs before any consumer client exists, so a rejected consumer creates nothing on the
    /// broker.
    /// </summary>
    /// <param name="consumerIdentity">The consumer identity, or the runtime subscription name, to name in the error.</param>
    internal void EnsureEveryInstanceSupported(string consumerIdentity)
    {
        var transport = _providersByRole.TryGetValue(MessagingProviderRole.Transport, out var transports)
            ? transports.SingleOrDefault()
            : null;

        if (transport is { SupportsEveryInstance: true })
        {
            return;
        }

        var provider = transport?.Provider ?? "(none declared)";
        throw new MessagingConfigurationException(
            $"Consumer '{consumerIdentity}' requires every-instance Bus delivery, which transport provider '{provider}' "
                + "does not support. Remove EveryInstance from the consumer, or select a transport that supports "
                + "every-instance subscriptions."
        );
    }

    /// <summary>
    /// Rejects a host that sends requests or declares a responder when the transport has no reply channel. Runs during
    /// bootstrap, before the reply listener and any processor start, so a rejected host never reports ready.
    /// </summary>
    /// <param name="responderIdentity">
    /// The identity of the responder to name in the error, or <see langword="null"/> when the host sends requests.
    /// </param>
    internal void EnsureRequestReplySupported(string? responderIdentity = null)
    {
        var transport = _providersByRole.TryGetValue(MessagingProviderRole.Transport, out var transports)
            ? transports.SingleOrDefault()
            : null;

        if (transport is { SupportsRequestReply: true })
        {
            return;
        }

        var provider = transport?.Provider ?? "(none declared)";
        var subject = responderIdentity is null
            ? "This host sends requests (AddRequestReply), which"
            : $"Consumer '{responderIdentity}' responds to requests, which";

        throw new MessagingConfigurationException(
            $"{subject} transport provider '{provider}' does not support. Request/reply needs a transport that "
                + "declares a reply channel; select one that supports request/reply, or use a Bus callback instead."
        );
    }

    /// <summary>Rejects an outbox publish when transport, storage, or scheduling support is absent.</summary>
    internal void EnsureOutboxSupported(MessageLane lane, bool scheduled)
    {
        EnsureDirectSupported(lane);

        if (
            !_providersByRole.TryGetValue(MessagingProviderRole.Storage, out var storageProviders)
            || storageProviders.Length != 1
            || !storageProviders[0].Lanes.Contains(lane)
        )
        {
            throw new MessagingConfigurationException(
                $"{lane} outbox delivery requires a matching storage capability contribution."
            );
        }

        if (scheduled && !storageProviders[0].SupportsDelayedScheduling)
        {
            throw new MessagingConfigurationException(
                $"Storage provider '{storageProviders[0].Provider}' does not support delayed {lane} scheduling."
            );
        }
    }

    private static void _ComposeTransport(
        IReadOnlyCollection<MessagingProviderCapabilities> declared,
        List<MessagingProviderCapabilities> providers
    )
    {
        var contributions = declared
            .Where(static capability => capability.Role == MessagingProviderRole.Transport)
            .ToArray();
        if (contributions.Length == 0)
        {
            return;
        }

        var providerNames = contributions
            .Select(static capability => capability.Provider)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (providerNames.Length != 1)
        {
            throw new MessagingConfigurationException(
                $"Messaging supports one transport provider identity; found: {string.Join(", ", providerNames.Order(StringComparer.Ordinal))}."
            );
        }

        var occupiedLanes = new HashSet<MessageLane>();
        foreach (var contribution in contributions)
        {
            foreach (var lane in contribution.Lanes)
            {
                if (!occupiedLanes.Add(lane))
                {
                    throw new MessagingConfigurationException(
                        $"Transport provider '{contribution.Provider}' has an overlapping duplicate {lane} capability contribution."
                    );
                }
            }
        }

        // Every-instance delivery is a Bus-lane property, so only the contributions that carry the Bus lane decide it, and
        // all of them must declare it; a Queue-only contribution has no say.
        var busContributions = contributions.Where(static x => x.Lanes.Contains(MessageLane.Bus)).ToArray();
        var supportsEveryInstance =
            busContributions.Length != 0 && busContributions.All(static x => x.SupportsEveryInstance);

        // Requests are Queue messages, so the same rule applies on the Queue lane: every contribution that carries it must
        // declare the reply channel, and a Bus-only contribution has no say.
        var queueContributions = contributions.Where(static x => x.Lanes.Contains(MessageLane.Queue)).ToArray();
        var supportsRequestReply =
            queueContributions.Length != 0 && queueContributions.All(static x => x.SupportsRequestReply);

        var topologyValues = contributions
            .Select(static capability => capability.SupportsIndependentLaneTopology)
            .Distinct()
            .ToArray();
        if (topologyValues.Length != 1)
        {
            throw new MessagingConfigurationException(
                $"Transport provider '{providerNames[0]}' contributed incompatible independent-lane topology declarations."
            );
        }

        providers.Add(
            MessagingProviderCapabilities.Transport(
                providerNames[0],
                occupiedLanes.ToArray(),
                topologyValues[0],
                contributions.SelectMany(static contribution => contribution.RoutingAffinityRoutes).ToArray(),
                supportsEveryInstance,
                supportsRequestReply
            )
        );
    }

    private static void _ComposeStorage(
        IReadOnlyCollection<MessagingProviderCapabilities> declared,
        List<MessagingProviderCapabilities> providers
    )
    {
        var contributions = declared
            .Where(static capability => capability.Role == MessagingProviderRole.Storage)
            .ToArray();
        if (contributions.Length == 0)
        {
            return;
        }

        if (contributions.Length != 1)
        {
            throw new MessagingConfigurationException(
                "Messaging requires exactly one storage provider capability contribution; multiple storage providers were configured."
            );
        }

        providers.Add(contributions[0]);
    }

    private void _RequireRole(MessagingProviderRole role, string message)
    {
        if (!_providersByRole.TryGetValue(role, out var providers) || providers.Length == 0)
        {
            throw new MessagingConfigurationException(message);
        }
    }

    private static void _EnsureDefinedLane(MessageLane lane)
    {
        Argument.IsInEnum(lane);
    }

    void IMessageCapabilityGate.ValidateStartup(
        IEnumerable<MessageRouteKey> routes,
        bool hasDurableConsumers,
        InboxGuarantee minimumInboxGuarantee
    ) => ValidateStartup(routes, hasDurableConsumers, minimumInboxGuarantee);

    void IMessageCapabilityGate.EnsureDirectSupported(MessageLane lane) => EnsureDirectSupported(lane);

    void IMessageCapabilityGate.EnsureRoutingAffinitySupported(
        string messageName,
        MessageLane lane,
        string key,
        IDictionary<string, string?> headers
    ) => EnsureRoutingAffinitySupported(messageName, lane, key, headers);

    void IMessageCapabilityGate.EnsureOutboxSupported(MessageLane lane, bool scheduled) =>
        EnsureOutboxSupported(lane, scheduled);

    void IMessageCapabilityGate.EnsureEveryInstanceSupported(string consumerIdentity) =>
        EnsureEveryInstanceSupported(consumerIdentity);

    void IMessageCapabilityGate.EnsureRequestReplySupported(string? responderIdentity) =>
        EnsureRequestReplySupported(responderIdentity);
}

/// <summary>Frozen provider capability authority used by bootstrap and direct publisher gates.</summary>
[PublicAPI]
public interface IMessagingCapabilityModel
{
    /// <summary>The inert declarations contributed before the service provider was built.</summary>
    IReadOnlyList<MessagingProviderCapabilities> DeclaredCapabilities { get; }

    /// <summary>Role/provider aggregates produced while freezing the model.</summary>
    IReadOnlyList<MessagingProviderCapabilities> Providers { get; }

    /// <summary>Always true for a composed model.</summary>
    bool IsFrozen { get; }

    /// <summary>The inbox guarantee declared by the configured storage provider, when one is present.</summary>
    InboxGuarantee? StorageInboxGuarantee { get; }

    /// <summary>The configured storage provider's capabilities, when one is present.</summary>
    MessagingProviderCapabilities? StorageProvider { get; }

    /// <summary>Returns whether a role supports a semantic lane.</summary>
    bool Supports(MessageLane lane, MessagingProviderRole role);
}

internal interface IMessageCapabilityGate : IMessagingCapabilityModel
{
    void ValidateStartup(
        IEnumerable<MessageRouteKey> routes,
        bool hasDurableConsumers,
        InboxGuarantee minimumInboxGuarantee
    );

    void EnsureDirectSupported(MessageLane lane);

    void EnsureRoutingAffinitySupported(
        string messageName,
        MessageLane lane,
        string key,
        IDictionary<string, string?> headers
    );

    void EnsureOutboxSupported(MessageLane lane, bool scheduled);

    void EnsureEveryInstanceSupported(string consumerIdentity);

    void EnsureRequestReplySupported(string? responderIdentity = null);
}
