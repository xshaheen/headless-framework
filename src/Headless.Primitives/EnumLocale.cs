// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A single localized enum value: its display name, optional description, and underlying value.</summary>
/// <typeparam name="T">The enum value type being localized.</typeparam>
[PublicAPI]
public sealed record EnumLocale<T>
{
    /// <summary>The localized display name for the value.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The optional localized description for the value.</summary>
    public string? Description { get; init; }

    /// <summary>The underlying enum value being localized.</summary>
    public required T Value { get; init; }
}
