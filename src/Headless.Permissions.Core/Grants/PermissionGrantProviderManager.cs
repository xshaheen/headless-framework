// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.GrantProviders;
using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MoreLinq.Extensions;

namespace Headless.Permissions.Grants;

/// <summary>Resolves and exposes the ordered list of active grant providers.</summary>
public interface IPermissionGrantProviderManager
{
    /// <summary>
    /// Grant providers ordered by registration priority; last-registered has the highest priority index.
    /// The built-in order (lowest to highest) is Role then User.
    /// </summary>
    IReadOnlyList<IPermissionGrantProvider> ValueProviders { get; }
}

/// <summary>
/// Default <see cref="IPermissionGrantProviderManager"/> implementation. Resolves providers from DI once
/// (lazy, thread-safe) and validates that no two providers share the same <see cref="IPermissionGrantProvider.Name"/>.
/// </summary>
/// <exception cref="InvalidOperationException">
/// Thrown on first access to <see cref="ValueProviders"/> when duplicate provider names are detected.
/// </exception>
public sealed class PermissionGrantProviderManager : IPermissionGrantProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PermissionManagementProvidersOptions _options;
    private readonly Lazy<List<IPermissionGrantProvider>> _lazyProviders;

    public IReadOnlyList<IPermissionGrantProvider> ValueProviders => _lazyProviders.Value;

    public PermissionGrantProviderManager(
        IServiceProvider serviceProvider,
        IOptions<PermissionManagementProvidersOptions> optionsAccessor
    )
    {
        _options = optionsAccessor.Value;
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
