namespace Framework.Settings.Definitions;

[PublicAPI]
public sealed class SettingDefinition(
    string name,
    string? defaultValue = null,
    string? displayName = null,
    string? description = null,
    bool isVisibleToClients = false,
    bool isInherited = true,
    bool isEncrypted = false
)
{
    /// <summary>Unique name of the setting.</summary>
    public string Name { get; } = name;

    /// <summary>Display name of the setting.</summary>
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

    /// <summary>
    /// Is the setting value is inherited from other providers or not. <see langword="true"/>
    /// means fallbacks to the next provider if the setting value was not set for the requested provider
    /// </summary>
    public bool IsInherited { get; init; } = isInherited;

    /// <summary>Is this setting stored as encrypted in the data source. Default: False.</summary>
    public bool IsEncrypted { get; set; } = isEncrypted;

    /// <summary>
    /// A list of allowed providers to get/set value of this setting.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; } = [];

    /// <summary>Can be used to get/set custom properties for this setting definition.</summary>
    public Dictionary<string, object?> Properties { get; } = [];

    /// <summary>Gets/sets a key-value on the <see cref="Properties"/>.</summary>
    /// <param name="name">Name of the property</param>
    /// <returns>
    /// Returns the value in the <see cref="Properties"/> dictionary by given <paramref name="name"/>.
    /// Returns null if given <paramref name="name"/> is not present in the <see cref="Properties"/> dictionary.
    /// </returns>
    public object? this[string name]
    {
        get => Properties.GetOrDefault(name);
        set => Properties[name] = value;
    }
}
