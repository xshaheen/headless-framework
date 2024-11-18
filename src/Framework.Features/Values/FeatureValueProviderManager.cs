// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Models;
using Framework.Features.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Framework.Features.Values;

public interface IFeatureValueProviderManager
{
    IReadOnlyList<IFeatureValueReadProvider> ValueProviders { get; }
}

public sealed class FeatureValueProviderManager : IFeatureValueProviderManager
{
    private readonly FeatureManagementProvidersOptions _providerOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly Lazy<List<IFeatureValueReadProvider>> _lazyProviders;

    public FeatureValueProviderManager(
        IServiceProvider serviceProvider,
        IOptions<FeatureManagementProvidersOptions> optionsAccessor
    )
    {
        _providerOptions = optionsAccessor.Value;
        _serviceProvider = serviceProvider;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

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
