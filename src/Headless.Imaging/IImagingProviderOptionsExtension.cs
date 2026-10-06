// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Imaging;

/// <summary>Defines the setup-time hook through which an imaging provider package registers its services.</summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessImagingSetupBuilder.RegisterExtension" />; <c>AddHeadlessImaging</c> calls
/// <see cref="AddServices" /> once per registered extension, after the core imaging services.
/// </remarks>
[PublicAPI]
public interface IImagingProviderOptionsExtension
{
    /// <summary>Registers the provider's contributors and options.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
