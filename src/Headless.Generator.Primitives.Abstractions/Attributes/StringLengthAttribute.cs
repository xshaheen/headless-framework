// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Validates that a string-backed primitive value length stays within minimum and maximum bounds.</summary>
/// <param name="minimumLength">The minimum allowed length.</param>
/// <param name="maximumLength">The maximum allowed length.</param>
/// <param name="validate">Indicates whether string length validation is enabled.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StringLengthAttribute(int minimumLength, int maximumLength, bool validate = true) : Attribute
{
    /// <summary>Gets the maximum allowed length.</summary>
    public int MaximumLength { get; } = maximumLength;

    /// <summary>Gets the minimum allowed length.</summary>
    public int MinimumLength { get; } = minimumLength;

    /// <summary>Gets a value indicating whether string length validation is enabled.</summary>
    public bool Validate { get; } = validate;
}
