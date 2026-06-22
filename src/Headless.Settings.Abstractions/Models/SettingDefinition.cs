// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Models;

/// <summary>Describes a named application setting, including its defaults, visibility, and storage options.</summary>
/// <param name="name">Unique name that identifies this setting.</param>
/// <param name="defaultValue">Optional default value used when no provider supplies a value.</param>
/// <param name="displayName">Human-readable name for UI surfaces; defaults to <c>name</c> when not specified.</param>
/// <param name="description">Optional descriptive text for the setting.</param>
/// <param name="isVisibleToClients">
/// Whether clients may read this setting's value. Defaults to <see langword="false"/> to prevent
/// accidental exposure of sensitive values such as passwords.
/// </param>
/// <param name="isInherited">
/// Whether the setting falls back to the next provider in the chain when no value is set.
/// Defaults to <see langword="true"/>.
/// </param>
/// <param name="isEncrypted">
/// Whether the value is stored encrypted in the data source. Defaults to <see langword="false"/>.
/// </param>
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

    /// <summary>Gets or sets a custom property value by name on <see cref="Properties"/>.</summary>
    /// <param name="name">Key of the property to get or set.</param>
    /// <returns>
    /// The value stored under <paramref name="name"/> in <see cref="Properties"/>,
    /// or <see langword="null"/> if <paramref name="name"/> is not present.
    /// </returns>
    public object? this[string name]
    {
        get => Properties.GetOrDefault(name);
        set => Properties[name] = value;
    }
}
