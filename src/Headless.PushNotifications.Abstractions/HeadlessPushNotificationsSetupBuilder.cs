// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Builder used by <c>AddHeadlessPushNotifications</c> to select exactly one push-notification provider.
/// </summary>
/// <remarks>
/// Provider packages contribute <c>Use{Provider}</c> extension members on this type (for example
/// <c>UseFirebase</c> from <c>Headless.PushNotifications.Firebase</c> and <c>UseNoop</c> from
/// <c>Headless.PushNotifications.Dev</c>). The push-notifications feature has no shared cross-provider options,
/// so the builder's only job is to collect the chosen provider for the one-provider gate.
/// </remarks>
[PublicAPI]
public sealed class HeadlessPushNotificationsSetupBuilder
{
    internal HeadlessPushNotificationsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IPushNotificationsProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>Registers a provider extension. Called by provider <c>Use{Provider}</c> members.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(IPushNotificationsProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
