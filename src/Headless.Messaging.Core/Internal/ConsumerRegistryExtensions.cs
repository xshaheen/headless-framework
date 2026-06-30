// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Registration;

namespace Headless.Messaging.Internal;

internal static class ConsumerRegistryExtensions
{
    public static TConfig? ResolveConsumerConfig<TConfig>(
        this IConsumerRegistry registry,
        string groupName,
        IntentType intentType
    )
        where TConfig : class
    {
        // For class-based configs (IProviderHeaderContributions) that hold a Func and cannot be
        // value-compared, deduplicate by runtime type — two instances of the same type for the
        // same group are idempotent. Records use their built-in value equality via the default path.
        var comparer = EqualityComparer<TConfig?>.Create(
            (x, y) =>
                x is IProviderHeaderContributions && y is IProviderHeaderContributions
                    ? x.GetType() == y.GetType()
                    : EqualityComparer<TConfig?>.Default.Equals(x, y),
            x =>
                x is IProviderHeaderContributions
                    ? x.GetType().GetHashCode()
                    : EqualityComparer<TConfig?>.Default.GetHashCode(x!)
        );

        var configs = registry
            .GetAll()
            .Where(consumer =>
                consumer.IntentType == intentType && string.Equals(consumer.Group, groupName, StringComparison.Ordinal)
            )
            .Select(consumer =>
                consumer.ProviderConfigs.TryGetValue(typeof(TConfig), out var config) ? config as TConfig : null
            )
            .Where(static config => config is not null)
            .Distinct(comparer)
            .ToArray();

        return configs.Length switch
        {
            0 => null,
            1 => configs[0],
            _ => throw new InvalidOperationException(
                $"Consumer group '{groupName}' has conflicting {typeof(TConfig).Name} provider configs."
            ),
        };
    }
}
