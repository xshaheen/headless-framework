// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Configuration;

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

    /// <summary>Returns whether a role supports a semantic lane.</summary>
    bool Supports(MessageLane lane, MessagingProviderRole role);
}

internal interface IMessageCapabilityGate : IMessagingCapabilityModel
{
    void ValidateStartup(IEnumerable<MessageRouteKey> routes);

    void EnsureDirectSupported(MessageLane lane);

    void EnsureOutboxSupported(MessageLane lane, bool scheduled);
}

/// <summary>Composes immutable provider contributions into the runtime capability authority.</summary>
[PublicAPI]
public sealed class MessagingCapabilityModel : IMessagingCapabilityModel, IMessageCapabilityGate
{
    private readonly FrozenDictionary<MessagingProviderRole, MessagingProviderCapabilities[]> _providersByRole;

    private MessagingCapabilityModel(
        MessagingProviderCapabilities[] declaredCapabilities,
        MessagingProviderCapabilities[] providers
    )
    {
        DeclaredCapabilities = Array.AsReadOnly(declaredCapabilities);
        Providers = Array.AsReadOnly(providers);
        _providersByRole = providers
            .GroupBy(static capability => capability.Role)
            .ToFrozenDictionary(static group => group.Key, static group => group.ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<MessagingProviderCapabilities> DeclaredCapabilities { get; }

    /// <inheritdoc />
    public IReadOnlyList<MessagingProviderCapabilities> Providers { get; }

    /// <inheritdoc />
    public bool IsFrozen => true;

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
        return _providersByRole.TryGetValue(role, out var providers)
            && providers.Any(provider => provider.Lanes.Contains(lane));
    }

    /// <summary>Validates the frozen model against every registered semantic route.</summary>
    internal void ValidateStartup(IEnumerable<MessageRouteKey> routes)
    {
        Argument.IsNotNull(routes);

        var routeArray = routes.ToArray();
        _RequireRole(MessagingProviderRole.Transport, "Messaging requires a transport provider contribution.");
        _RequireRole(MessagingProviderRole.Storage, "Messaging requires exactly one storage provider contribution.");

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

    /// <summary>Rejects a direct publish when the selected lane has no declared transport capability.</summary>
    internal void EnsureDirectSupported(MessageLane lane)
    {
        _EnsureDefinedLane(lane);
        if (Supports(lane, MessagingProviderRole.Transport))
        {
            return;
        }

        throw new MessagingConfigurationException(
            $"{lane} direct delivery is unsupported by the declared transport capabilities. "
                + "Register the provider through AddMessagingProviderCapabilities; raw transport registrations are not capability evidence."
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
            MessagingProviderCapabilities.Transport(providerNames[0], occupiedLanes.ToArray(), topologyValues[0])
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

    void IMessageCapabilityGate.ValidateStartup(IEnumerable<MessageRouteKey> routes) => ValidateStartup(routes);

    void IMessageCapabilityGate.EnsureDirectSupported(MessageLane lane) => EnsureDirectSupported(lane);

    void IMessageCapabilityGate.EnsureOutboxSupported(MessageLane lane, bool scheduled) =>
        EnsureOutboxSupported(lane, scheduled);
}
