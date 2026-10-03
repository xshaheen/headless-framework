// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>
/// Immutable declaration of the behavior a messaging provider can actually support.
/// Provider implementations contribute this value during service registration; runtime behavior is never inferred by
/// resolving transport or storage services.
/// </summary>
[PublicAPI]
public sealed record MessagingProviderCapabilities
{
    private MessagingProviderCapabilities(
        string provider,
        MessagingProviderRole role,
        IEnumerable<MessageLane> lanes,
        bool supportsIndependentLaneTopology,
        bool supportsDelayedScheduling,
        MessagingInboxCapabilityTier? inboxCapability,
        IReadOnlyCollection<MessagingRoutingAffinityRoute>? routingAffinityRoutes = null,
        bool supportsEveryInstance = false,
        bool supportsRequestReply = false
    )
    {
        Argument.IsNotNullOrWhiteSpace(provider);
        Provider = provider;
        Role = role;
        Lanes = lanes.Select(_EnsureDefinedLane).ToFrozenSet();
        SupportsIndependentLaneTopology = supportsIndependentLaneTopology;
        SupportsDelayedScheduling = supportsDelayedScheduling;
        InboxCapability = inboxCapability;
        RoutingAffinityRoutes = Array.AsReadOnly((routingAffinityRoutes ?? []).ToArray());
        SupportsEveryInstance = supportsEveryInstance;
        SupportsRequestReply = supportsRequestReply;

        if (supportsEveryInstance && !Lanes.Contains(MessageLane.Bus))
        {
            throw new ArgumentException(
                "Only a transport contribution that declares the Bus lane can support every-instance subscriptions.",
                nameof(supportsEveryInstance)
            );
        }

        if (supportsRequestReply && !Lanes.Contains(MessageLane.Queue))
        {
            throw new ArgumentException(
                "Only a transport contribution that declares the Queue lane can support request/reply, because requests "
                    + "are Queue messages.",
                nameof(supportsRequestReply)
            );
        }

        if (Lanes.Count == 0 && role is not MessagingProviderRole.Coordination)
        {
            throw new ArgumentException(
                "Transport and storage capability descriptors require at least one lane.",
                nameof(lanes)
            );
        }

        if (role is MessagingProviderRole.Storage)
        {
            Argument.IsInEnum(inboxCapability!.Value);
        }
        else if (inboxCapability is not null)
        {
            throw new ArgumentException(
                "Only storage capability descriptors may declare an inbox tier.",
                nameof(inboxCapability)
            );
        }
    }

    /// <summary>Stable provider identifier used by setup diagnostics and conformance evidence.</summary>
    public string Provider { get; }

    /// <summary>Provider role represented by this contribution.</summary>
    public MessagingProviderRole Role { get; }

    /// <summary>Semantic lanes supported by this contribution.</summary>
    public FrozenSet<MessageLane> Lanes { get; }

    /// <summary>
    /// Whether the transport keeps a shared contract/logical name physically independent across Bus and Queue.
    /// </summary>
    public bool SupportsIndependentLaneTopology { get; }

    /// <summary>Whether persisted delivery can be scheduled for a future dispatch time.</summary>
    public bool SupportsDelayedScheduling { get; }

    /// <summary>
    /// Strongest inbox guarantee supplied by this provider, or <see langword="null"/> for non-storage roles.
    /// </summary>
    public MessagingInboxCapabilityTier? InboxCapability { get; }

    /// <summary>Locally verified registered destinations with native affinity mappings; empty means unsupported.</summary>
    public IReadOnlyList<MessagingRoutingAffinityRoute> RoutingAffinityRoutes { get; }

    /// <summary>
    /// Whether the transport can give each process a Bus subscription of its own that the broker removes once the
    /// process no longer holds it, as every-instance consumers require.
    /// </summary>
    public bool SupportsEveryInstance { get; }

    /// <summary>
    /// Whether the transport registers an <see cref="Headless.Messaging.Transport.IReplyTransport"/>, a reply channel addressed to one
    /// process, and passes the shared request/reply conformance suite. A host that sends requests or declares a
    /// responder fails at startup on a transport without it.
    /// </summary>
    public bool SupportsRequestReply { get; }

    /// <summary>Creates an immutable transport capability contribution.</summary>
    /// <param name="provider">The stable provider identifier.</param>
    /// <param name="lanes">The semantic lanes the transport carries.</param>
    /// <param name="supportsIndependentLaneTopology">
    /// Whether a shared logical name stays physically independent across Bus and Queue.
    /// </param>
    /// <param name="routingAffinityRoutes">The registered destinations with native affinity mappings.</param>
    /// <param name="supportsEveryInstance">
    /// Whether the transport supports every-instance Bus subscriptions; requires the Bus lane in
    /// <paramref name="lanes"/>.
    /// </param>
    /// <param name="supportsRequestReply">
    /// Whether the transport registers a reply transport and passes the request/reply conformance suite; requires the
    /// Queue lane in <paramref name="lanes"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="lanes"/> is empty or undefined, <paramref name="supportsEveryInstance"/> is set without the Bus
    /// lane, or <paramref name="supportsRequestReply"/> is set without the Queue lane.
    /// </exception>
    public static MessagingProviderCapabilities Transport(
        string provider,
        IReadOnlyCollection<MessageLane> lanes,
        bool supportsIndependentLaneTopology,
        IReadOnlyCollection<MessagingRoutingAffinityRoute>? routingAffinityRoutes = null,
        bool supportsEveryInstance = false,
        bool supportsRequestReply = false
    )
    {
        Argument.IsNotNull(lanes);
        return new MessagingProviderCapabilities(
            provider,
            MessagingProviderRole.Transport,
            lanes,
            supportsIndependentLaneTopology,
            supportsDelayedScheduling: false,
            inboxCapability: null,
            routingAffinityRoutes,
            supportsEveryInstance,
            supportsRequestReply
        );
    }

    /// <summary>Creates an immutable storage capability contribution.</summary>
    public static MessagingProviderCapabilities Storage(
        string provider,
        IReadOnlyCollection<MessageLane> lanes,
        bool supportsDelayedScheduling,
        MessagingInboxCapabilityTier inboxCapability
    )
    {
        Argument.IsNotNull(lanes);
        return new MessagingProviderCapabilities(
            provider,
            MessagingProviderRole.Storage,
            lanes,
            supportsIndependentLaneTopology: true,
            supportsDelayedScheduling,
            inboxCapability
        );
    }

    /// <summary>Creates an immutable coordination capability contribution.</summary>
    public static MessagingProviderCapabilities Coordination(string provider)
    {
        return new MessagingProviderCapabilities(
            provider,
            MessagingProviderRole.Coordination,
            [],
            supportsIndependentLaneTopology: true,
            supportsDelayedScheduling: false,
            inboxCapability: null
        );
    }

    private static MessageLane _EnsureDefinedLane(MessageLane lane)
    {
        return Argument.IsInEnum(lane);
    }
}
