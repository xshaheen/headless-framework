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
