// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Configures a named push notification service instance.
/// </summary>
[PublicAPI]
public sealed class HeadlessPushNotificationsInstanceBuilder
{
    internal HeadlessPushNotificationsInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>Gets the service instance name used for service keys and named options.</summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    /// <summary>Registers the provider delegate for this instance.</summary>
    /// <param name="action">The delegate that registers services into the service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A provider is already registered for this instance.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RegisterProvider(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        if (Action is not null)
        {
            throw new InvalidOperationException(
                $"Multiple providers were configured for named push-notification service '{Name}'."
            );
        }

        Action = action;
    }
}
