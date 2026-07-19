// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Settings.Models;

/// <summary>Specifies the metadata used to create a setting definition.</summary>
[PublicAPI]
public sealed class SettingDefinitionCreateOptions
{
    /// <summary>Gets the unique setting name.</summary>
    /// <exception cref="ArgumentNullException">The initialized value is <see langword="null"/>.</exception>
    public required string Name
    {
        get;
        init => field = Argument.IsNotNull(value);
    }

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
