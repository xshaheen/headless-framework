// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Associates a localized display name (and optional description) for a specific locale with an enum field or enum
/// type. May be applied multiple times to provide translations for several locales.
/// </summary>
/// <param name="locale">The locale key (for example a culture name) this localization applies to.</param>
/// <param name="displayName">The localized display name for the annotated member in the given <paramref name="locale"/>.</param>
/// <param name="description">An optional localized description for the annotated member.</param>
[PublicAPI]
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
