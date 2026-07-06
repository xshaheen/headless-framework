// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Models;

/// <summary>Holds the resolved value of a named setting along with the provider that supplied it.</summary>
/// <param name="Name">Unique name of the setting.</param>
/// <param name="Value">The resolved string value, or <see langword="null"/> if no provider supplied a value.</param>
/// <param name="Provider">
/// The value provider that resolved the value, or <see langword="null"/> when the setting has no value.
/// The default of <see langword="null"/> lets internal name/value carriers construct without attribution.
/// </param>
[PublicAPI]
public sealed record SettingValue(string Name, string? Value, SettingValueProvider? Provider = null);

/// <summary>Identifies the value provider that supplied a setting value.</summary>
/// <param name="Name">The provider name (e.g., <c>Global</c>, <c>Tenant</c>, <c>User</c>, <c>DefaultValue</c>).</param>
/// <param name="Key">The provider-specific key (e.g., a tenant ID or user ID), or <see langword="null"/> when not applicable.</param>
[PublicAPI]
public sealed record SettingValueProvider(string Name, string? Key);
