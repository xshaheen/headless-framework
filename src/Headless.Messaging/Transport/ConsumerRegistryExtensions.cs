// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>Reads the provider configs an application attached to declared consumers.</summary>
public static class ConsumerRegistryExtensions
{
    /// <summary>
    /// Returns the <typeparamref name="TConfig"/> attached through <see cref="IConsumerProviderConfigBuilder"/> to the
    /// consumers of one broker subscription, so a consumer client applies per-consumer settings such as a prefetch
    /// count when it opens the subscription.
    /// </summary>
    /// <remarks>
    /// Several consumers can share one subscription. Equal configs, or two configs of one type that implements
    /// <see cref="IProviderHeaderContributions"/>, count as one; any other difference is a conflict.
    /// </remarks>
    /// <typeparam name="TConfig">The provider's consumer config type.</typeparam>
    /// <param name="registry">The consumer registry.</param>
    /// <param name="subscriptionName">The subscription the consumer client opens.</param>
    /// <param name="lane">The lane of the subscription.</param>
    /// <returns>The config, or <see langword="null"/> when no consumer of the subscription has one.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registry"/> or <paramref name="subscriptionName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The subscription's consumers carry conflicting configs.</exception>
    public static TConfig? ResolveConsumerConfig<TConfig>(
        this IConsumerRegistry registry,
        string subscriptionName,
        MessageLane lane
    )
        where TConfig : class
    {
        Argument.IsNotNull(registry);
        Argument.IsNotNull(subscriptionName);

        // For class-based configs (IProviderHeaderContributions) that hold a Func and cannot be
        // value-compared, deduplicate by runtime type — two instances of the same type for the
        // same subscription are idempotent. Records use their built-in value equality via the default path.
        var comparer = EqualityComparer<TConfig?>.Create(
            (x, y) =>
                x is IProviderHeaderContributions && y is IProviderHeaderContributions
                    ? x.GetType() == y.GetType()
                    : EqualityComparer<TConfig?>.Default.Equals(x, y),
            x =>
                x is IProviderHeaderContributions
                    ? x.GetType().GetHashCode()
#pragma warning disable RCS1249 // Unnecessary null-forgiving operator
                    : EqualityComparer<TConfig?>.Default.GetHashCode(x!)
#pragma warning restore RCS1249
        );

        var configs = registry
            .GetAll()
            .Where(consumer =>
                consumer.Lane == lane
                && string.Equals(consumer.SubscriptionName, subscriptionName, StringComparison.Ordinal)
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
                $"Subscription '{subscriptionName}' has conflicting {typeof(TConfig).Name} provider configs."
            ),
        };
    }
}
