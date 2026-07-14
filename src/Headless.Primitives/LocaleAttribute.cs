// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Associates a localized display name (and optional description) for a specific locale with an enum field or enum
/// type. May be applied multiple times to provide translations for several locales.
/// </summary>
/// <param name="locale">The locale key (for example a culture name) this localization applies to.</param>
/// <param name="displayName">The localized display name for the annotated member in the given <paramref name="locale"/>.</param>
/// <param name="description">An optional localized description for the annotated member.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Enum, AllowMultiple = true)]
#pragma warning disable CA1813 // Avoid unsealed attributes. Justification: Needs to be inherited to create specific locale attributes.
public class LocaleAttribute(string locale, string displayName, string? description = null) : Attribute
#pragma warning restore CA1813
{
    /// <summary>The locale key this localization applies to.</summary>
    public string Locale { get; } = locale;

    /// <summary>The localized display name for the annotated member.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>The optional localized description for the annotated member.</summary>
    public string? Description { get; } = description;
}

/// <summary>The complete set of localized values for an enum: the default locale plus per-locale entries.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
/// <param name="Default">The localization for the default locale.</param>
/// <param name="Locales">The localizations for each additional locale, keyed by locale.</param>
public sealed record AllLocaleValue<T>(EnumLocale<T> Default, KeyEnumLocale<T>[] Locales);

/// <summary>A localized enum value tagged with the locale key it belongs to.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
public sealed record KeyEnumLocale<T>
{
    /// <summary>The locale key this localization belongs to.</summary>
    public required string Key { get; init; }

    /// <summary>The localized enum value for <see cref="Key"/>.</summary>
    public required EnumLocale<T> Locale { get; init; }
}

/// <summary>A single localized enum value: its display name, optional description, and underlying value.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
public sealed record EnumLocale<T>
{
    /// <summary>The localized display name for the value.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The optional localized description for the value.</summary>
    public string? Description { get; init; }

    /// <summary>The underlying enum value being localized.</summary>
    public required T Value { get; init; }
}
