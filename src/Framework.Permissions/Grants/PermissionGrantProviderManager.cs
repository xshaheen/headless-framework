// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.GrantProviders;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq.Extensions;

namespace Framework.Permissions.Grants;

public interface IPermissionGrantProviderManager
{
    IReadOnlyList<IPermissionGrantProvider> ValueProviders { get; }
}

public sealed class PermissionGrantProviderManager : IPermissionGrantProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PermissionManagementProvidersOptions _options;
    private readonly Lazy<List<IPermissionGrantProvider>> _lazyProviders;

    public IReadOnlyList<IPermissionGrantProvider> ValueProviders => _lazyProviders.Value;

    public PermissionGrantProviderManager(
        IServiceProvider serviceProvider,
        IOptions<PermissionManagementProvidersOptions> options
    )
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _lazyProviders = new Lazy<List<IPermissionGrantProvider>>(_GetProviders, isThreadSafe: true);
    }

    private List<IPermissionGrantProvider> _GetProviders()
    {
        var providers = _options
            .GrantProviders.Select(type => (IPermissionGrantProvider)_serviceProvider.GetRequiredService(type))
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
