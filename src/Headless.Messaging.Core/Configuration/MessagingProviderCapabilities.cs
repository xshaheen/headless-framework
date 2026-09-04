// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>Identifies the messaging subsystem role supplied by a provider contribution.</summary>
[PublicAPI]
public enum MessagingProviderRole
{
    Transport = 0,
    Storage = 1,
    Coordination = 2,
}

/// <summary>Describes the strongest inbox guarantee a storage provider can enforce.</summary>
[PublicAPI]
public enum MessagingInboxCapabilityTier
{
    /// <summary>State and duplicate suppression are process-local and do not survive restart.</summary>
    ProcessLocal = 0,

    /// <summary>Inbox state is durable, but its outcome cannot commit atomically with application state.</summary>
    DurableDedupeOnly = 1,

    /// <summary>
    /// Inbox outcome, compatible enlisted application state, and captured outgoing work can commit atomically.
    /// </summary>
    Transactional = 2,
}

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
        MessagingInboxCapabilityTier? inboxCapability
    )
    {
        Argument.IsNotNullOrWhiteSpace(provider);
        Provider = provider;
        Role = role;
        Lanes = lanes.Select(_EnsureDefinedLane).ToFrozenSet();
        SupportsIndependentLaneTopology = supportsIndependentLaneTopology;
        SupportsDelayedScheduling = supportsDelayedScheduling;
        InboxCapability = inboxCapability;

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

    /// <summary>Creates an immutable transport capability contribution.</summary>
    public static MessagingProviderCapabilities Transport(
        string provider,
        IReadOnlyCollection<MessageLane> lanes,
        bool supportsIndependentLaneTopology
    )
    {
        Argument.IsNotNull(lanes);
        return new MessagingProviderCapabilities(
            provider,
            MessagingProviderRole.Transport,
            lanes,
            supportsIndependentLaneTopology,
            supportsDelayedScheduling: false,
            inboxCapability: null
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
