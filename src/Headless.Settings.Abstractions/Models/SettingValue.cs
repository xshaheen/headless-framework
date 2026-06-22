// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Models;

/// <summary>Holds the resolved value of a named setting.</summary>
/// <param name="Name">Unique name of the setting.</param>
public sealed record SettingValue(string Name)
{
    /// <summary>Initializes a <see cref="SettingValue"/> with both a name and a value.</summary>
    /// <param name="name">Unique name of the setting.</param>
    /// <param name="value">The resolved string value, or <see langword="null"/> if unset.</param>
    public SettingValue(string name, string? value)
        : this(name) => Value = value;

    /// <summary>The resolved setting value, or <see langword="null"/> if the setting has no value.</summary>
    public string? Value { get; set; }
}
