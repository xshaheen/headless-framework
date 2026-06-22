// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Abstract base that implements structural equality for a type by delegating comparison and hashing to
/// strongly-typed hooks rather than reference identity.
/// </summary>
/// <remarks>
/// Subclasses override <see cref="EqualityComponentsEqual"/> (compare each equality-defining field to the
/// other instance) and <see cref="BuildHashCode"/> (feed each equality-defining field into the hash). Both
/// run without boxing value-type components, unlike an <c>IEnumerable&lt;object?&gt;</c> component sequence.
/// Two instances of the same concrete type are equal when every component compares equal.
/// </remarks>
/// <typeparam name="T">The concrete subclass; used to constrain the typed <c>Equals</c> overload.</typeparam>
[PublicAPI]
public abstract class EqualityBase<T> : IEquatable<T>
    where T : EqualityBase<T>
{
    /// <summary>
    /// Determines whether this instance is equal to <paramref name="other"/> by comparing their runtime
    /// types and equality components.
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

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are structurally equal.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public static bool operator ==(EqualityBase<T>? left, EqualityBase<T>? right)
    {
        return left is null ? right is null : left.Equals(right as T);
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="left"/> and <paramref name="right"/> are not structurally equal.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
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
    /// <returns>A combined hash code consistent with <c>Equals</c>.</returns>
    public sealed override int GetHashCode()
    {
        var hash = new HashCode();
        BuildHashCode(ref hash);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Compares this instance's equality components to <paramref name="other"/>. The caller guarantees
    /// <paramref name="other"/> is non-null and has the same runtime type as this instance, so implementations
    /// may cast it to the concrete type and compare fields directly without boxing.
    /// </summary>
    /// <param name="other">The same-typed instance to compare against.</param>
    /// <returns><see langword="true"/> when every equality component is equal; otherwise <see langword="false"/>.</returns>
    protected abstract bool EqualityComponentsEqual(T other);

    /// <summary>
    /// Feeds each equality-defining component into <paramref name="hash"/>. Add the same components, in the
    /// same set, that <see cref="EqualityComponentsEqual"/> compares, so equal instances hash equally.
    /// </summary>
    /// <param name="hash">The hash accumulator to add components to.</param>
    protected abstract void BuildHashCode(ref HashCode hash);
}
