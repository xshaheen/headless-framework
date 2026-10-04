// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Configures push notification providers and named instances.
/// </summary>
[PublicAPI]
public sealed class HeadlessPushNotificationsSetupBuilder
{
    private readonly HashSet<string> _instanceNames = new(StringComparer.Ordinal);

    internal HeadlessPushNotificationsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IReadOnlySet<string> InstanceNames => _instanceNames;

    internal List<Action<IServiceCollection>> DefaultExtensions { get; } = [];

    internal List<(string Name, Action<IServiceCollection> Action)> NamedExtensions { get; } = [];

    /// <summary>
    /// Queues the default push notification provider registration.
    /// </summary>
    /// <param name="action">The delegate that registers services into the service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RegisterDefaultProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        DefaultExtensions.Add(action);
    }

    /// <summary>
    /// Adds a named push notification service instance.
    /// </summary>
    /// <param name="name">The unique service instance name.</param>
    /// <param name="configure">A delegate that configures the named provider.</param>
    /// <returns>The builder instance for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="name"/> is already configured, or the configuration delegate does not select exactly one provider.
    /// </exception>
    public HeadlessPushNotificationsSetupBuilder AddNamed(
        string name,
        Action<HeadlessPushNotificationsInstanceBuilder> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        if (!_instanceNames.Add(name))
        {
            throw new InvalidOperationException($"A named push-notification service '{name}' is already configured.");
        }

        var instance = new HeadlessPushNotificationsInstanceBuilder(name);
        configure(instance);

        if (instance.Action is null)
        {
            throw new InvalidOperationException(
                $"Named push-notification service '{name}' requires exactly one provider. "
                    + "Call one of `UseApns`, `UseFirebase`, or `UseNoop`."
            );
        }

        NamedExtensions.Add((name, instance.Action));

        return this;
    }
}
