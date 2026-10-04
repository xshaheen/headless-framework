// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Provides an abstract base that implements structural equality by delegating comparison and hashing to
/// strongly typed hooks rather than reference identity.
/// </summary>
/// <remarks>
/// Subclasses override <see cref="EqualityComponentsEqual"/> and <see cref="BuildHashCode"/>. Both methods execute
/// without boxing value-type components. Two instances of the same concrete type are equal when every component compares equal.
/// </remarks>
/// <typeparam name="T">The concrete subclass type.</typeparam>
[PublicAPI]
public abstract class EqualityBase<T> : IEquatable<T>
    where T : EqualityBase<T>
{
    /// <summary>
    /// Determines whether this instance is equal to <paramref name="other"/> by comparing runtime types and equality components.
    /// </summary>
    /// <param name="other">The instance to compare against, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if both instances have the same type and equal components; otherwise <see langword="false"/>.</returns>
    public bool Equals(T? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && EqualityComponentsEqual(other);
    }

    /// <summary>Determines whether two instances are equal.</summary>
    /// <param name="left">The left operand, or <see langword="null"/>.</param>
    /// <param name="right">The right operand, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(EqualityBase<T>? left, EqualityBase<T>? right)
    {
        return left is null ? right is null : left.Equals(right as T);
    }

    /// <summary>Determines whether two instances are not equal.</summary>
    /// <param name="left">The left operand, or <see langword="null"/>.</param>
    /// <param name="right">The right operand, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(EqualityBase<T>? left, EqualityBase<T>? right)
    {
        return !(left == right);
    }

    /// <inheritdoc/>
    public sealed override bool Equals(object? obj)
    {
        return Equals(obj as T);
    }

    /// <summary>Computes a hash code from all equality components.</summary>
    /// <returns>A combined hash code consistent with <see cref="Equals(T)"/>.</returns>
    public sealed override int GetHashCode()
    {
        var hash = new HashCode();
        BuildHashCode(ref hash);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Compares equality components of this instance to <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The same-typed instance to compare against.</param>
    /// <returns><see langword="true"/> when every equality component is equal; otherwise <see langword="false"/>.</returns>
    protected abstract bool EqualityComponentsEqual(T other);

    /// <summary>
    /// Adds equality-defining components to <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The hash accumulator.</param>
#pragma warning disable CA1045 // Do not pass types by reference
    protected abstract void BuildHashCode(ref HashCode hash);
#pragma warning restore CA1045
}
