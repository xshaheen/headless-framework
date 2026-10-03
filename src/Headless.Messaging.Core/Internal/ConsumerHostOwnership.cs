// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class ConsumerHostOwnership(ConsumerRegistry registry, IRuntimeConsumerRegistry runtimeConsumers)
    : IConsumerHostOwnership
{
    public bool Starts(ConsumerMetadata consumer) => registry.ConsumeFilter.Allows(consumer);

    public IReadOnlyCollection<string>? GetRetriedIdentities()
    {
        var consumed = registry.ConsumeFilter.ConsumedIdentities;
        if (consumed is null)
        {
            return null;
        }

        HashSet<string>? identities = null;
        foreach (var descriptor in runtimeConsumers.GetDescriptors())
        {
            if (descriptor.EveryInstance)
            {
                continue;
            }

            identities ??= new HashSet<string>(consumed, StringComparer.Ordinal);
            identities.Add(descriptor.ResolvedConsumerIdentity);
        }

        // Keep the filter's ordinal order so a provider that binds the set as a query parameter sees a stable value.
        return identities is null || identities.Count == consumed.Count
            ? consumed
            : [.. identities.Order(StringComparer.Ordinal)];
    }
}

/// <summary>
/// Answers which consumers and durable identities this host runs. It is the one place <c>ConsumeOnly</c>, the
/// every-instance exemption, and the runtime subscriptions attached to the host combine, so starting consumer clients and
/// picking up received rows for retry always agree.
/// </summary>
/// <remarks>
/// Every-instance consumers are never filtered: each process owns a subscription for per-process state such as a local
/// cache, so a host that skipped one would silently keep stale state. They store no rows, so they never add a retried
/// identity. <c>ConsumeOnly</c> never filters a runtime subscription either, and a competing one stores its rows under its
/// resolved identity, which only the host holding its delegate can run.
/// </remarks>
internal interface IConsumerHostOwnership
{
    /// <summary>Whether this host starts a consumer client for the registered <paramref name="consumer"/>.</summary>
    bool Starts(ConsumerMetadata consumer);

    /// <summary>
    /// The consumer identities whose received rows this host retries, in ordinal order, or <see langword="null"/> when it
    /// retries every identity. Storage providers bind it as the received-row pickup predicate, so a filtered host never
    /// leases a retry or orphan probe for a consumer it has no executor for.
    /// </summary>
    /// <remarks>
    /// Read per retry cycle: runtime subscriptions attach and detach while the host runs.
    /// </remarks>
    IReadOnlyCollection<string>? GetRetriedIdentities();
}
