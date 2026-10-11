// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.IpGeolocation;

/// <summary>Chooses the IP geolocation provider inside <c>AddHeadlessIpGeolocation</c>.</summary>
[PublicAPI]
public sealed class HeadlessIpGeolocationSetupBuilder
{
    internal HeadlessIpGeolocationSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IIpGeolocationProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>Adds a provider. Provider packages call this from their <c>Use*</c> member.</summary>
    /// <param name="extension">The provider's registration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(IIpGeolocationProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
