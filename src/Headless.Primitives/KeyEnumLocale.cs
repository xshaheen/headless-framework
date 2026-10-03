// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A localized enum value tagged with the locale key it belongs to.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
[PublicAPI]
public sealed record KeyEnumLocale<T>
{
    /// <summary>The locale key this localization belongs to.</summary>
    public required string Key { get; init; }

    /// <summary>The localized enum value for <see cref="Key"/>.</summary>
    public required EnumLocale<T> Locale { get; init; }
}
