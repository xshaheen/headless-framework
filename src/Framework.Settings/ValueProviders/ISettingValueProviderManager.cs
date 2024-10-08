// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Framework.Settings.ValueProviders;

/// <summary>Manage list of setting value providers.</summary>
public interface ISettingValueProviderManager
{
    List<ISettingValueProvider> Providers { get; }
}

/// <inheritdoc />
public sealed class SettingValueProviderManager : ISettingValueProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FrameworkSettingOptions _options;
    private readonly Lazy<List<ISettingValueProvider>> _lazyProviders;

    public SettingValueProviderManager(IServiceProvider serviceProvider, IOptions<FrameworkSettingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

    public List<ISettingValueProvider> Providers => _lazyProviders.Value;

    /// <summary>Retrieves a list of setting value providers from the service provider.</summary>
    /// <exception cref="InvalidOperationException">Thrown when there are duplicate setting value provider names.</exception>
    private List<ISettingValueProvider> _GetProviders()
    {
        var providers = _options
            .ValueProviders.Select(type => (ISettingValueProvider)_serviceProvider.GetRequiredService(type))
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
            $"Duplicate setting value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{providersText}"
        );
    }
}
