// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.IpGeolocation;

/// <summary>Registers a provider's services when <c>AddHeadlessIpGeolocation</c> runs.</summary>
/// <remarks>Provider packages implement this; applications choose a provider through its <c>Use*</c> member.</remarks>
[PublicAPI]
public interface IIpGeolocationProviderOptionsExtension
{
    /// <summary>Adds the provider's services to <paramref name="services" />.</summary>
    /// <param name="services">The service collection being configured.</param>
    void AddServices(IServiceCollection services);
}
