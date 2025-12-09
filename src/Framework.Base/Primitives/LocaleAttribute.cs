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

public sealed record AllLocaleValue<T>(EnumLocale<T> Default, KeyEnumLocale<T>[] Locales);

public sealed record KeyEnumLocale<T>
{
    public required string Key { get; init; }

    public required EnumLocale<T> Locale { get; init; }
}

public sealed record EnumLocale<T>
{
    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required T Value { get; init; }
}
