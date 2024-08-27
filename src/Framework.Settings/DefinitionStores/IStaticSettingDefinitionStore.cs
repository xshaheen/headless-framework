using Framework.Settings.DefinitionProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Settings.DefinitionStores;

/// <summary>Retrieves setting definitions from a service provider and <see cref="FrameworkSettingOptions.DefinitionProviders"/></summary>
public interface IStaticSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

/// <inheritdoc />
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
        return Task.FromResult<IReadOnlyList<SettingDefinition>>(_settingDefinitions.Value.Values.ToImmutableList());
    }

    public Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        return Task.FromResult(_settingDefinitions.Value.GetOrDefault(name));
    }

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
