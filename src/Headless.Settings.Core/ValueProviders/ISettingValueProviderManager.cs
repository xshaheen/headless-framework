// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Headless.Settings.ValueProviders;

/// <summary>Manage a list of setting value providers.</summary>
public interface ISettingValueProviderManager
{
    IReadOnlyList<ISettingValueReadProvider> Providers { get; }
}

/// <inheritdoc />
public sealed class SettingValueProviderManager : ISettingValueProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingManagementProvidersOptions _options;
    private readonly Lazy<List<ISettingValueReadProvider>> _lazyProviders;

    public SettingValueProviderManager(
        IServiceProvider serviceProvider,
        IOptions<SettingManagementProvidersOptions> optionsAccessor
    )
    {
        _serviceProvider = serviceProvider;
        _options = optionsAccessor.Value;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

    public IReadOnlyList<ISettingValueReadProvider> Providers => _lazyProviders.Value;

    /// <summary>Retrieves a list of setting value providers from the service provider.</summary>
    /// <exception cref="InvalidOperationException">Thrown when there are duplicate setting value provider names.</exception>
    private List<ISettingValueReadProvider> _GetProviders()
    {
        var providers = _options
            .ValueProviders.Select(type => (ISettingValueReadProvider)_serviceProvider.GetRequiredService(type))
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
            $"Duplicate setting value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{providersText}"
        );
    }
}
