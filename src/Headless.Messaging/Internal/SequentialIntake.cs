// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

/// <summary>How one host at a time comes to take in a sequential subscription's deliveries.</summary>
/// <remarks>
/// A sequential consumer orders by the order its deliveries are admitted in. Inside one host one client admits them one
/// at a time; across hosts, each running a client, the broker would split the stream between them and each host would
/// order only its share.
/// </remarks>
internal enum SequentialIntake
{
    /// <summary>The subscription is not sequential, or belongs to this process alone.</summary>
    NotSequential = 0,

    /// <summary>The transport routes every message the subscription consumes to one consumer by its key.</summary>
    KeyRouted = 1,

    /// <summary>The broker delivers the subscription to one consumer at a time.</summary>
    BrokerSingleActiveConsumer = 2,

    /// <summary>The host that holds the subscription's messaging distributed lock is the one that listens.</summary>
    DistributedLock = 3,

    /// <summary>No lock is configured, so every host listens and each orders only the share it receives.</summary>
    Unguarded = 4,
}

internal static class SequentialIntakeResolver
{
    /// <summary>Decides how one host at a time takes in the subscription's deliveries.</summary>
    /// <param name="key">The subscription.</param>
    /// <param name="descriptors">The consumers the subscription delivers to.</param>
    /// <param name="transport">The composed transport capability, or <see langword="null"/> when none is declared.</param>
    /// <param name="distributedLockConfigured">
    /// Whether the messaging-scoped lock provider is a real one; see <see cref="IsDistributedLockConfigured"/>.
    /// </param>
    public static SequentialIntake Resolve(
        ConsumerSubscriptionKey key,
        IReadOnlyCollection<ConsumerExecutorDescriptor> descriptors,
        MessagingProviderCapabilities? transport,
        bool distributedLockConfigured
    )
    {
        return Resolve(
            key.Lane,
            key.Kind is ConsumerSubscriptionKind.Competing && descriptors.Any(static d => d.Sequential),
            descriptors.Select(static d => d.MessageName),
            transport,
            distributedLockConfigured
        );
    }

    /// <summary>Decides how one host at a time takes in a competing subscription's deliveries.</summary>
    /// <param name="lane">The lane the subscription consumes.</param>
    /// <param name="sequential">Whether the subscription is competing and one of its consumers is sequential.</param>
    /// <param name="messageNames">The messages the subscription consumes.</param>
    /// <param name="transport">The composed transport capability, or <see langword="null"/> when none is declared.</param>
    /// <param name="distributedLockConfigured">
    /// Whether the messaging-scoped lock provider is a real one; see <see cref="IsDistributedLockConfigured"/>.
    /// </param>
    public static SequentialIntake Resolve(
        MessageLane lane,
        bool sequential,
        IEnumerable<string> messageNames,
        MessagingProviderCapabilities? transport,
        bool distributedLockConfigured
    )
    {
        if (!sequential)
        {
            return SequentialIntake.NotSequential;
        }

        // A key route sends each key to one partition, FIFO group, or session, which one consumer reads in order, so
        // hosts can share the subscription without breaking any key's order. Every message must have one: a message
        // without a route could reach any host.
        if (
            transport is not null
            && messageNames.All(messageName =>
                transport.RoutingAffinityRoutes.Any(route =>
                    route.Lane == lane && string.Equals(route.MessageName, messageName, StringComparison.Ordinal)
                )
            )
        )
        {
            return SequentialIntake.KeyRouted;
        }

        if (transport is { SupportsSingleActiveConsumer: true })
        {
            return SequentialIntake.BrokerSingleActiveConsumer;
        }

        return distributedLockConfigured ? SequentialIntake.DistributedLock : SequentialIntake.Unguarded;
    }

    /// <summary>Whether <paramref name="lockProvider"/> can elect one host, which the no-op lock cannot.</summary>
    /// <remarks>
    /// Decided by type, never by a failed acquire: the no-op lock grants every acquire, so it would make every host
    /// think it is the only one listening.
    /// </remarks>
    public static bool IsDistributedLockConfigured(IDistributedLock lockProvider) =>
        lockProvider is not NullDistributedLock;

    /// <summary>The value the host manifest reports for <paramref name="intake"/>.</summary>
    public static string? ManifestValue(SequentialIntake intake) =>
        intake switch
        {
            SequentialIntake.KeyRouted => "keyRouted",
            SequentialIntake.BrokerSingleActiveConsumer => "broker",
            SequentialIntake.DistributedLock => "lock",
            SequentialIntake.Unguarded => "none",
            _ => null,
        };
}
