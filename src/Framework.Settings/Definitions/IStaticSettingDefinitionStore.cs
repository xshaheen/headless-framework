// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Settings.Definitions;

/// <summary>Store for setting definitions that are defined statically in memory.</summary>
public interface IStaticSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

public sealed class StaticSettingDefinitionStore : IStaticSettingDefinitionStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FrameworkSettingOptions _options;
    private readonly Lazy<Dictionary<string, SettingDefinition>> _settingDefinitions;

    public StaticSettingDefinitionStore(IServiceProvider serviceProvider, IOptions<FrameworkSettingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _settingDefinitions = new(_CreateSettingDefinitions, isThreadSafe: true);
    }

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        var settingDefinitions = _settingDefinitions.Value.Values.ToImmutableList();

        return Task.FromResult<IReadOnlyList<SettingDefinition>>(settingDefinitions);
    }

    public Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        var settingDefinition = _settingDefinitions.Value.GetOrDefault(name);

        return Task.FromResult(settingDefinition);
    }

    private Dictionary<string, SettingDefinition> _CreateSettingDefinitions()
    {
        var settings = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        var context = new SettingDefinitionContext(settings);

#pragma warning disable MA0045
        using var scope = _serviceProvider.CreateScope();
#pragma warning restore MA0045

        foreach (var type in _options.DefinitionProviders)
        {
            var provider = (ISettingDefinitionProvider)scope.ServiceProvider.GetRequiredService(type);
            provider.Define(context);
        }

        return settings;
    }
}
