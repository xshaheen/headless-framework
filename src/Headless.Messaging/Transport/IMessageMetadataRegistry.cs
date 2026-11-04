// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The catalog of declared message routes, merged from every contract registration: for each route, its contract
/// version, correlation selector, provider configs, and whether it needs routing affinity or accepts raw payloads.
/// </summary>
/// <remarks>
/// <para>
/// The core builds and registers the catalog; a transport resolves it from the container to learn which routes to
/// subscribe, which need key-ordered delivery, and which provider configs an application attached to a contract.
/// Providers read it and do not implement it.
/// </para>
/// <para>
/// <b>Evolution policy:</b> new members arrive as default interface members, and existing members never gain
/// parameters.
/// </para>
/// </remarks>
public interface IMessageMetadataRegistry
{
    /// <summary>Returns every declared route.</summary>
    IReadOnlyCollection<MessageMetadata> GetAll();

    /// <summary>Looks up one route.</summary>
    /// <param name="route">The route to look up.</param>
    /// <param name="metadata">The route's metadata when it is declared; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="route"/> is declared.</returns>
    bool TryGet(MessageRouteKey route, [NotNullWhen(true)] out MessageMetadata? metadata);
}

/// <summary>One declared message route and what its registrations attached to it.</summary>
/// <remarks>The core creates these; a new fact about a route arrives as another read-only property.</remarks>
public sealed record MessageMetadata
{
    internal MessageMetadata(
        MessageRouteKey route,
        Type messageType,
        string contractVersion,
        Func<object, string?>? correlationSelector,
        IReadOnlyDictionary<Type, object> providerConfigs,
        bool requiresRoutingAffinity = false,
        bool rawPayload = false
    )
    {
        Route = route;
        MessageType = messageType;
        ContractVersion = contractVersion;
        CorrelationSelector = correlationSelector;
        ProviderConfigs = providerConfigs;
        RequiresRoutingAffinity = requiresRoutingAffinity;
        RawPayload = rawPayload;
    }

    /// <summary>Gets the route: contract type, resolved message name, and lane.</summary>
    public MessageRouteKey Route { get; }

    /// <summary>Gets the contract type.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the contract version stamped on published messages.</summary>
    public string ContractVersion { get; }

    /// <summary>Gets the selector that reads a correlation identifier from a payload, when one was declared.</summary>
    public Func<object, string?>? CorrelationSelector { get; }

    /// <summary>
    /// Gets the provider configs attached to the contract, keyed by config type. A provider finds its own by type.
    /// </summary>
    public IReadOnlyDictionary<Type, object> ProviderConfigs { get; }

    /// <summary>Gets whether the route asked for key-ordered delivery.</summary>
    public bool RequiresRoutingAffinity { get; }

    /// <summary>Gets whether the route accepts raw payloads from producers that send no Headless envelope.</summary>
    public bool RawPayload { get; }
}

/// <summary>Lane-qualified identity of a declared contract routed by its logical message name.</summary>
/// <param name="ContractType">The contract type.</param>
/// <param name="MessageName">The resolved message name, with any configured prefix applied.</param>
/// <param name="Lane">The lane the route belongs to.</param>
/// <remarks>A route is exactly these three values; the key never gains a member.</remarks>
public readonly record struct MessageRouteKey(Type ContractType, string MessageName, MessageLane Lane);
