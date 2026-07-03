// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Root builder for <c>AddHeadlessPushNotifications</c>. Provider packages contribute deferred service
/// registrations into two slots — an optional default service (at most one, the unkeyed
/// <see cref="IPushNotificationService"/>) and named instances (unlimited, unique names, resolved as keyed
/// <see cref="IPushNotificationService"/> services or through <see cref="IPushNotificationServiceProvider"/>).
/// Nothing is registered into <see cref="Services"/> until the setup gates pass; contributions are queued only.
/// </summary>
/// <remarks>
/// Push notifications has no shared, cross-provider feature options, so the builder is provider-selection-only
/// and carries no <c>Configure</c> overloads. Each provider binds its own options inside its <c>Use*</c> member.
/// </remarks>
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
    /// Queues the default (unkeyed) push-notification service contribution. Called internally by each default
    /// <c>Use*</c> extension; not intended for direct use by application code.
    /// </summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
    public void RegisterDefaultProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        DefaultExtensions.Add(action);
    }

    /// <summary>
    /// Adds an independently-configured named push-notification service, resolvable as a keyed
    /// <see cref="IPushNotificationService"/> service or through <see cref="IPushNotificationServiceProvider"/>.
    /// Named instances never touch the default (unkeyed) <see cref="IPushNotificationService"/>.
    /// </summary>
    /// <param name="name">The service instance name. Must be non-empty and unique within this call.</param>
    /// <param name="configure">Configuration action that selects exactly one provider for the instance.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="name"/> is already configured, or when the instance selects zero or more
    /// than one provider.
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
                    + "Call one of `UseFirebase` or `UseNoop`."
            );
        }

        NamedExtensions.Add((name, instance.Action));

        return this;
    }
}
