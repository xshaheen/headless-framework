namespace Primitives;

/// <summary>Represents an interface for primitive values.</summary>
public interface IPrimitive
{
    /// <summary>Gets the underlying primitive type of the primitive value.</summary>
    /// <returns>The underlying primitive type of the primitive value.</returns>
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
    /// <summary>Gets the underlying primitive value of the primitive value.</summary>
    /// <returns>The underlying primitive value of the primitive value.</returns>
    T GetUnderlyingPrimitiveValue();

    /// <summary>
    /// Validates the specified value against primitive-specific rules and returns a validation result.
    /// </summary>
    /// <param name="value">The value to be validated against primitive constraints.</param>
    static abstract PrimitiveValidationResult Validate(T value);

    /// <summary>
    /// Retrieves a string representation of the specified primitive value.
    /// </summary>
    /// <param name="value">The primitive value to be represented as a string.</param>
    /// <returns>A string representation of the primitive value.</returns>
    static virtual string ToString(T value) => value.ToString() ?? string.Empty;
}
