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
/// Defines a contract for domain-specific values ensuring type safety and constraints.
/// This interface serves as a foundation for encapsulating and validating domain-specific values.
/// </summary>
/// <typeparam name="T">The type of the primitive value.</typeparam>
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
