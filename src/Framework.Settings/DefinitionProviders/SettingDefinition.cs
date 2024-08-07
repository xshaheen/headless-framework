namespace Framework.Settings.DefinitionProviders;

public sealed class SettingDefinition(
    string name,
    string? defaultValue = null,
    string? displayName = null,
    string? description = null,
    bool isVisibleToClients = false,
    bool isEncrypted = false
)
{
    /// <summary>Unique name of the setting.</summary>
    public string Name { get; } = name;

    public string DisplayName { get; set; } = displayName ?? name;

    /// <summary>Setting description.</summary>
    public string? Description { get; set; } = description;

    /// <summary>Default value of the setting.</summary>
    public string? DefaultValue { get; set; } = defaultValue;

    /// <summary>
    /// Can clients see this setting and it's value. It maybe dangerous for some settings
    /// to be visible to clients (such as an email server password). Default: false.
    /// </summary>
    public bool IsVisibleToClients { get; set; } = isVisibleToClients;

    /// <summary>Is this setting stored as encrypted in the data source. Default: False.</summary>
    public bool IsEncrypted { get; set; } = isEncrypted;

    /// <summary>
    /// A list of allowed providers to get/set value of this setting.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; } = [];

    /// <summary>Can be used to get/set custom properties for this setting definition.</summary>
    public Dictionary<string, object> Properties { get; } = [];

    public SettingDefinition WithProperty(string key, object value)
    {
        Properties[key] = value;

        return this;
    }

    public SettingDefinition WithProviders(params string[] providers)
    {
        if (!providers.IsNullOrEmpty())
        {
            Providers.AddIfNotContains(providers);
        }

        return this;
    }
}
