// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Framework.Features.Values;

public interface IFeatureValueProviderManager
{
    IReadOnlyList<IFeatureValueProvider> ValueProviders { get; }
}

public sealed class FeatureValueProviderManager : IFeatureValueProviderManager
{
    private readonly FrameworkFeatureOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly Lazy<List<IFeatureValueProvider>> _lazyProviders;

    public FeatureValueProviderManager(IServiceProvider serviceProvider, IOptions<FrameworkFeatureOptions> options)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

    public IReadOnlyList<IFeatureValueProvider> ValueProviders => _lazyProviders.Value;

    private List<IFeatureValueProvider> _GetProviders()
    {
        var providers = _options
            .ValueProviders.Select(type => (IFeatureValueProvider)_serviceProvider.GetRequiredService(type))
            .ToList();

        var multipleProviders = providers
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .FirstOrDefault(x => x.AtLeast(2));

        if (multipleProviders is null)
        {
            return providers;
        }

        throw new InvalidOperationException(
            $"Duplicate feature value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{multipleProviders.Select(p => p.GetType().FullName!).JoinAsString(Environment.NewLine)}"
        );
    }
}
