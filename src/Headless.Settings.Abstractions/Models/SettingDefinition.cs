// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Settings.Models;

/// <summary>Describes a named application setting, including its defaults, visibility, and storage options.</summary>
/// <remarks>
/// Instances are minted through <see cref="ISettingDefinitionContext.Add(SettingDefinitionCreateOptions)"/>;
/// the constructor is <see langword="internal"/> so the definition registry owns creation.
/// </remarks>
[PublicAPI]
public sealed class SettingDefinition : IHasExtraProperties
{
    /// <summary>Initializes a new <see cref="SettingDefinition"/>.</summary>
    /// <param name="name">Unique name that identifies this setting.</param>
    /// <param name="defaultValue">Optional default value used when no provider supplies a value.</param>
    /// <param name="displayName">Human-readable name for UI surfaces; defaults to <paramref name="name"/> when not specified.</param>
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
    internal SettingDefinition(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = false,
        bool isInherited = true,
        bool isEncrypted = false
    )
    {
        Name = name;
        DefaultValue = defaultValue;
        DisplayName = displayName ?? name;
        Description = description;
        IsVisibleToClients = isVisibleToClients;
        IsInherited = isInherited;
        IsEncrypted = isEncrypted;
    }

    /// <summary>Unique name of the setting.</summary>
    public string Name { get; }

    /// <summary>Display name of the setting. Never <see langword="null"/>.</summary>
    [field: AllowNull, MaybeNull]
    public string DisplayName
    {
        get;
        set => field = Argument.IsNotNull(value);
    }

    /// <summary>Setting description.</summary>
    public string? Description { get; set; }

    /// <summary>Default value of the setting.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Can clients see this setting and it's value. It maybe dangerous for some settings
    /// to be visible to clients (such as an email server password). Default: false.
    /// </summary>
    public bool IsVisibleToClients { get; set; }

    /// <summary>
    /// Is the setting value is inherited from other providers or not. <see langword="true"/>
    /// means fallbacks to the next provider if the setting value was not set for the requested provider
    /// </summary>
    public bool IsInherited { get; set; }

    /// <summary>Is this setting stored as encrypted in the data source. Default: False.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// A list of allowed providers to get/set value of this setting.
    /// An empty list indicates that all providers are allowed.
    /// </summary>
    public List<string> Providers { get; } = [];

    /// <summary>Bag of custom properties for this setting definition.</summary>
    public ExtraProperties ExtraProperties { get; } = [];

    /// <summary>Gets or sets a custom property value by name on <see cref="ExtraProperties"/>.</summary>
    /// <param name="name">Key of the property to get or set.</param>
    /// <returns>
    /// The value stored under <paramref name="name"/> in <see cref="ExtraProperties"/>,
    /// or <see langword="null"/> if <paramref name="name"/> is not present.
    /// </returns>
    public object? this[string name]
    {
        get => ExtraProperties.GetOrDefault(name);
        set => ExtraProperties[name] = value;
    }
}
