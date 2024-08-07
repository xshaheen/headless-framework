namespace Framework.Settings.DefinitionProviders;

public interface ISettingDefinitionContext
{
    SettingDefinition? GetOrNull(string name);

    IReadOnlyList<SettingDefinition> GetAll();

    void Add(params SettingDefinition[] definitions);
}

public sealed class SettingDefinitionContext(Dictionary<string, SettingDefinition> settings) : ISettingDefinitionContext
{
    public SettingDefinition? GetOrNull(string name)
    {
        return settings.GetOrDefault(name);
    }

    public IReadOnlyList<SettingDefinition> GetAll()
    {
        return settings.Values.ToImmutableList();
    }

    public void Add(params SettingDefinition[] definitions)
    {
        if (definitions.IsNullOrEmpty())
        {
            return;
        }

        foreach (var definition in definitions)
        {
            settings[definition.Name] = definition;
        }
    }
}
