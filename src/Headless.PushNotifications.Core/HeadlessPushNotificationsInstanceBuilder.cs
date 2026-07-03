// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Builder for a single named push-notification service inside <c>AddHeadlessPushNotifications</c>. Provider
/// packages contribute exactly one provider per instance through <see cref="RegisterProvider"/> (called by each
/// instance-scoped <c>Use*</c> extension, for example <c>UseFirebase</c> or <c>UseNoop</c>).
/// </summary>
[PublicAPI]
public sealed class HeadlessPushNotificationsInstanceBuilder
{
    internal HeadlessPushNotificationsInstanceBuilder(string name)
    {
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>The service instance name. Used as the keyed-service key and the named-options name.</summary>
    public string Name { get; }

    internal Action<IServiceCollection>? Action { get; private set; }

    /// <summary>Captures the provider contribution for this instance. Must be called exactly once.</summary>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a provider is already registered for this instance.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)] // provider-package plumbing, not an application-code API
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
