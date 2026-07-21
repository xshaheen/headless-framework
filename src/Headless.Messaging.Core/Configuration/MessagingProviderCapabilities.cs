// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>Identifies the messaging subsystem role supplied by a provider contribution.</summary>
[PublicAPI]
public enum MessagingProviderRole
{
    Transport,
    Storage,
    Coordination,
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
        bool supportsDelayedScheduling
    )
    {
        Argument.IsNotNullOrWhiteSpace(provider);
        Provider = provider;
        Role = role;
        Lanes = lanes.Select(_EnsureDefinedLane).ToFrozenSet();
        SupportsIndependentLaneTopology = supportsIndependentLaneTopology;
        SupportsDelayedScheduling = supportsDelayedScheduling;

        if (Lanes.Count == 0 && role is not MessagingProviderRole.Coordination)
        {
            throw new ArgumentException(
                "Transport and storage capability descriptors require at least one lane.",
                nameof(lanes)
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
            supportsDelayedScheduling: false
        );
    }

    /// <summary>Creates an immutable storage capability contribution.</summary>
    public static MessagingProviderCapabilities Storage(
        string provider,
        IReadOnlyCollection<MessageLane> lanes,
        bool supportsDelayedScheduling
    )
    {
        Argument.IsNotNull(lanes);
        return new MessagingProviderCapabilities(
            provider,
            MessagingProviderRole.Storage,
            lanes,
            supportsIndependentLaneTopology: true,
            supportsDelayedScheduling
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
            supportsDelayedScheduling: false
        );
    }

    private static MessageLane _EnsureDefinedLane(MessageLane lane)
    {
        return Argument.IsInEnum(lane);
    }
}
