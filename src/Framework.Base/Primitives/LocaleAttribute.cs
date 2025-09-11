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

public record ValueLocale
{
    public required string Locale { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required int Value { get; init; }
}
