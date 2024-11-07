// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Models;
using Framework.Permissions.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq.Extensions;

namespace Framework.Permissions.Values;

public interface IPermissionValueProviderManager
{
    IReadOnlyList<IPermissionValueProvider> ValueProviders { get; }
}

public sealed class PermissionValueProviderManager : IPermissionValueProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PermissionManagementProvidersOptions _options;
    private readonly Lazy<List<IPermissionValueProvider>> _lazyProviders;

    public IReadOnlyList<IPermissionValueProvider> ValueProviders => _lazyProviders.Value;

    public PermissionValueProviderManager(
        IServiceProvider serviceProvider,
        IOptions<PermissionManagementProvidersOptions> options
    )
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _lazyProviders = new Lazy<List<IPermissionValueProvider>>(_GetProviders, isThreadSafe: true);
    }

    private List<IPermissionValueProvider> _GetProviders()
    {
        var providers = _options
            .ValueProviders.Select(type => (IPermissionValueProvider)_serviceProvider.GetRequiredService(type))
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
            $"Duplicate permission value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{providersText}"
        );
    }
}
