// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;
using Headless.Features.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Headless.Features.Values;

/// <summary>Provides ordered access to all registered <see cref="IFeatureValueReadProvider"/> instances.</summary>
/// <remarks>
/// Providers are returned in priority order (highest priority first), which is the reverse of their
/// registration order. The default built-in chain is <c>Tenant</c> → <c>Edition</c> → <c>DefaultValue</c>.
/// </remarks>
public interface IFeatureValueProviderManager
{
    /// <summary>Gets the ordered list of value providers, highest priority first.</summary>
    IReadOnlyList<IFeatureValueReadProvider> ValueProviders { get; }
}

/// <summary>
/// Default implementation of <see cref="IFeatureValueProviderManager"/> that resolves providers from the DI container
/// on first access and validates that no two providers share the same name.
/// </summary>
public sealed class FeatureValueProviderManager : IFeatureValueProviderManager
{
    private readonly FeatureManagementProvidersOptions _providerOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly Lazy<List<IFeatureValueReadProvider>> _lazyProviders;

    /// <summary>Initializes a new instance of <see cref="FeatureValueProviderManager"/>.</summary>
    /// <param name="serviceProvider">The application service provider used to resolve provider instances.</param>
    /// <param name="optionsAccessor">Options that specify the ordered list of provider types.</param>
    public FeatureValueProviderManager(
        IServiceProvider serviceProvider,
        IOptions<FeatureManagementProvidersOptions> optionsAccessor
    )
    {
        _providerOptions = optionsAccessor.Value;
        _serviceProvider = serviceProvider;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Two or more registered providers share the same <see cref="IFeatureValueReadProvider.Name"/>.</exception>
    public IReadOnlyList<IFeatureValueReadProvider> ValueProviders => _lazyProviders.Value;

    private List<IFeatureValueReadProvider> _GetProviders()
    {
        var providers = _providerOptions
            .ValueProviders.Select(type => (IFeatureValueReadProvider)_serviceProvider.GetRequiredService(type))
            .Reverse()
            .ToList();

        var multipleProviders = providers
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .FirstOrDefault(x => x.AtLeast(2));

        if (multipleProviders is null)
        {
            return providers;
        }

        var providersText = multipleProviders.Select(p => p.GetType().FullName!).JoinAsString(Environment.NewLine);

        throw new InvalidOperationException(
            $"Duplicate feature value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{providersText}"
        );
    }
}
