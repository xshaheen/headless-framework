// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Setup-time extension hook contributed by a push-notification provider package (Firebase, no-op, ...).
/// </summary>
/// <remarks>
/// A provider's <c>Use{Provider}</c> builder method registers one implementation of this interface on the
/// <see cref="HeadlessPushNotificationsSetupBuilder"/>; <see cref="AddServices"/> runs later from
/// <c>AddHeadlessPushNotifications</c>, after the exactly-one-provider gate has passed.
/// </remarks>
[PublicAPI]
public interface IPushNotificationsProviderOptionsExtension
{
    /// <summary>Registers the provider's services into the container.</summary>
    void AddServices(IServiceCollection services);
}
