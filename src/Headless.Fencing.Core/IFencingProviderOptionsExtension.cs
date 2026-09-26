// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Fencing;

/// <summary>Setup-time hook through which a fencing provider package registers its services.</summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessFencingSetupBuilder.RegisterExtension" />; <c>AddHeadlessFencing</c> calls
/// <see cref="AddServices" /> once, after checking that exactly one provider was chosen.
/// </remarks>
[PublicAPI]
public interface IFencingProviderOptionsExtension
{
    /// <summary>Registers the provider's store, options, storage initializer, and unit-of-work support.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
