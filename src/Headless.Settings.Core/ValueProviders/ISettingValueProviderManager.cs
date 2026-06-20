// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq;

namespace Headless.Settings.ValueProviders;

/// <summary>Manages the ordered list of registered <see cref="ISettingValueReadProvider"/> instances.</summary>
public interface ISettingValueProviderManager
{
    /// <summary>Gets the ordered list of registered setting value providers, highest priority last.</summary>
    IReadOnlyList<ISettingValueReadProvider> Providers { get; }
}

/// <summary>Default manager for resolving configured setting value providers.</summary>
public sealed class SettingValueProviderManager : ISettingValueProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingManagementProvidersOptions _options;
    private readonly Lazy<List<ISettingValueReadProvider>> _lazyProviders;

    /// <summary>Initialises a new <see cref="SettingValueProviderManager"/>.</summary>
    /// <param name="serviceProvider">Used to resolve each registered provider type.</param>
    /// <param name="optionsAccessor">Options that list the provider types in registration order.</param>
    public SettingValueProviderManager(
        IServiceProvider serviceProvider,
        IOptions<SettingManagementProvidersOptions> optionsAccessor
    )
    {
        _serviceProvider = serviceProvider;
        _options = optionsAccessor.Value;
        _lazyProviders = new(_GetProviders, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISettingValueReadProvider> Providers => _lazyProviders.Value;

    /// <summary>Resolves and validates all configured provider instances.</summary>
    /// <exception cref="InvalidOperationException">Two or more registered providers share the same <see cref="ISettingValueReadProvider.Name"/>.</exception>
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
