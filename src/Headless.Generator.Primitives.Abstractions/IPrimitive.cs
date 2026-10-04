// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Generator.Primitives;

/// <summary>Defines the contract for primitive types.</summary>
public interface IPrimitive
{
    /// <summary>Gets the underlying primitive type.</summary>
    /// <returns>The underlying primitive type.</returns>
    Type GetUnderlyingPrimitiveType();
}

/// <summary>
/// Defines a contract for domain-specific primitive values ensuring type safety and constraints.
/// </summary>
/// <typeparam name="T">The underlying primitive type.</typeparam>
public interface IPrimitive<T> : IPrimitive
    where T : IEquatable<T>, IComparable, IComparable<T>
{
    /// <summary>Gets the underlying primitive value.</summary>
    /// <returns>The underlying value.</returns>
    T GetUnderlyingPrimitiveValue();

    /// <summary>
    /// Validates the value against primitive constraints.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>The validation outcome.</returns>
    static abstract PrimitiveValidationResult Validate(T value);

    /// <summary>
    /// Formats the primitive value as a string representation.
    /// </summary>
    /// <param name="value">The primitive value to format.</param>
    /// <returns>The formatted string representation.</returns>
    static virtual string ToString(T value)
    {
        return value.ToString() ?? string.Empty;
    }
}
