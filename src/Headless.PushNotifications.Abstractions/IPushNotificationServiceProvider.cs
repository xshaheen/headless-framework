// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Resolves named <see cref="IPushNotificationService"/> instances registered by the application.
/// </summary>
[PublicAPI]
public interface IPushNotificationServiceProvider
{
    /// <summary>Gets the push notification service registered under <paramref name="name"/>.</summary>
    /// <param name="name">The service instance name.</param>
    /// <returns>The resolved push notification service.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">No service is registered under <paramref name="name"/>.</exception>
    IPushNotificationService GetService(string name);

    /// <summary>Gets the push notification service registered under <paramref name="name"/>, or <see langword="null"/> when not found.</summary>
    /// <param name="name">The service instance name.</param>
    /// <returns>The resolved push notification service, or <see langword="null"/> when not found.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    IPushNotificationService? GetServiceOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named push notification service instances.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
