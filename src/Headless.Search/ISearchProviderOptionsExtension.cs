// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Search;

/// <summary>Defines the setup-time hook through which a search provider package registers its services.</summary>
/// <remarks>
/// A provider's <c>Use…</c> member registers one instance with
/// <see cref="HeadlessSearchSetupBuilder.RegisterExtension" />; <c>AddHeadlessSearch</c> calls
/// <see cref="AddServices" /> once, after checking that exactly one provider was chosen.
/// </remarks>
[PublicAPI]
public interface ISearchProviderOptionsExtension
{
    /// <summary>Registers the provider's options and its schema contribution.</summary>
    /// <param name="services">The application service collection.</param>
    void AddServices(IServiceCollection services);
}
