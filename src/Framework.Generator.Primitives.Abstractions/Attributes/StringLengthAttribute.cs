// ReSharper disable once CheckNamespace

namespace Primitives;

/// <summary>Validation attribute to assert a string property, field or parameter does not exceed a maximum length</summary>
/// <param name="minimumLength">The minimum length allowed for the string.</param>
/// <param name="maximumLength">The maximum length allowed for the string.</param>
/// <param name="validate">Indicates whether the string length should be validated.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StringLengthAttribute(int minimumLength, int maximumLength, bool validate = true) : Attribute
{
    /// <summary>Gets the maximum length allowed for the string.</summary>
    public int MaximumLength { get; } = maximumLength;

    /// <summary>Gets the minimum length allowed for the string.</summary>
    public int MinimumLength { get; } = minimumLength;

    /// <summary>Gets a value indicating whether the string length should be validated.</summary>
    public bool Validate { get; } = validate;
}
