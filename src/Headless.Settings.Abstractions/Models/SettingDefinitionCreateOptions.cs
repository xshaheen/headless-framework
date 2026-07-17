// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Settings.Models;

/// <summary>Specifies the metadata used to create a setting definition.</summary>
[PublicAPI]
public sealed class SettingDefinitionCreateOptions
{
    /// <summary>Creates options for a setting with the specified unique name.</summary>
    /// <param name="name">The unique setting name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public SettingDefinitionCreateOptions(string name)
    {
        Name = Argument.IsNotNull(name);
    }

    /// <summary>Gets the unique setting name.</summary>
    public string Name { get; }

    /// <summary>Gets the optional default value used when no provider supplies a value.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets the optional display name. When omitted, <see cref="Name"/> is used.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the optional setting description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether clients may read the setting value. The default is <see langword="false"/>.</summary>
    public bool IsVisibleToClients { get; init; }

    /// <summary>Gets whether the setting falls back through the provider chain. The default is <see langword="true"/>.</summary>
    public bool IsInherited { get; init; } = true;

    /// <summary>Gets whether the setting is stored encrypted. The default is <see langword="false"/>.</summary>
    public bool IsEncrypted { get; init; }
}
