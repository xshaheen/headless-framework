// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.Definitions;

/// <summary>
/// Default implementation of <see cref="IStaticSettingDefinitionStore"/>. Builds the definition
/// dictionary once (thread-safe lazy) by invoking all registered <see cref="ISettingDefinitionProvider"/>
/// instances in a transient DI scope.
/// </summary>
/// <remarks>
/// Duplicate setting names registered by different providers are silently overwritten by the last
/// provider to define them; providers are invoked in the order declared in
/// <see cref="SettingManagementProvidersOptions.DefinitionProviders"/>.
/// </remarks>
public sealed class StaticSettingDefinitionStore : IStaticSettingDefinitionStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SettingManagementProvidersOptions _options;
    private readonly Lazy<Dictionary<string, SettingDefinition>> _settingDefinitions;

    /// <summary>Initializes a new instance of <see cref="StaticSettingDefinitionStore"/>.</summary>
    /// <param name="serviceProvider">Used to resolve <see cref="ISettingDefinitionProvider"/> instances.</param>
    /// <param name="optionsAccessor">Options that list the registered definition providers.</param>
    public StaticSettingDefinitionStore(
        IServiceProvider serviceProvider,
        IOptions<SettingManagementProvidersOptions> optionsAccessor
    )
    {
        _serviceProvider = serviceProvider;
        _options = optionsAccessor.Value;
        _settingDefinitions = new(_CreateSettingDefinitions, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settingDefinitions = _settingDefinitions.Value.Values.ToImmutableList();

        return Task.FromResult<IReadOnlyList<SettingDefinition>>(settingDefinitions);
    }

    /// <inheritdoc/>
    public Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settingDefinition = _settingDefinitions.Value.GetOrDefault(name);

        return Task.FromResult(settingDefinition);
    }

    /// <summary>
    /// Builds the in-memory setting definitions dictionary by invoking all registered
    /// <see cref="ISettingDefinitionProvider"/> instances within a transient DI scope.
    /// </summary>
    private Dictionary<string, SettingDefinition> _CreateSettingDefinitions()
    {
        var settings = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        var context = new SettingDefinitionContext(settings);

        using var scope = _serviceProvider.CreateScope();

        foreach (var type in _options.DefinitionProviders)
        {
            var provider = (ISettingDefinitionProvider)scope.ServiceProvider.GetRequiredService(type);
            provider.Define(context);
        }

        return settings;
    }
}
