// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Enum, AllowMultiple = true)]
#pragma warning disable CA1813 // Avoid unsealed attributes. Justification: Needs to be inherited to create specific locale attributes.
public class LocaleAttribute(string locale, string displayName, string? description = null) : Attribute
#pragma warning restore CA1813
{
    public string Locale { get; } = locale;

    public string DisplayName { get; } = displayName;

    public string? Description { get; } = description;
}

public sealed record AllLocaleValue(EnumLocale Default, KeyEnumLocale[] Locales);

public record KeyEnumLocale
{
    public required string Key { get; init; }

    public required EnumLocale Locale { get; init; }
}

public record EnumLocale
{
    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required int Value { get; init; }
}
