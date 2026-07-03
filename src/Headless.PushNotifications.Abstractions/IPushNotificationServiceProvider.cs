// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// Resolves <see cref="IPushNotificationService"/> instances registered under a name through the setup builder —
/// for example <c>setup.AddNamed("marketing", i =&gt; i.UseFirebase(…))</c>. The default (unkeyed) service is
/// resolved directly as <see cref="IPushNotificationService"/> and is not exposed through this provider.
/// </summary>
[PublicAPI]
public interface IPushNotificationServiceProvider
{
    /// <summary>Gets the push-notification service registered under <paramref name="name"/>.</summary>
    /// <param name="name">The service instance name.</param>
    /// <returns>The resolved push-notification service.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no service is registered under <paramref name="name"/>; the message points to <c>AddNamed</c>
    /// and the provider <c>Use*</c> methods.
    /// </exception>
    IPushNotificationService GetService(string name);

    /// <summary>Gets the push-notification service registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The service instance name.</param>
    /// <returns>The resolved push-notification service, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    IPushNotificationService? GetServiceOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named push-notification service instances. Use this to validate an
    /// externally-supplied name before resolving it, rather than probing <see cref="GetServiceOrNull"/> and
    /// handling <see langword="null"/>. The default (unnamed) service is not included.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
