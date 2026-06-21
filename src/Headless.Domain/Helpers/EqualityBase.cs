// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Abstract base that implements structural equality for a type by delegating comparison
/// to a sequence of <c>EqualityComponents()</c> values rather than reference identity.
/// </summary>
/// <remarks>
/// Subclasses must override <c>EqualityComponents()</c> and return all fields that define equality.
/// Two instances of the same concrete type are considered equal when every component compares equal in order.
/// </remarks>
/// <typeparam name="T">The concrete subclass; used to constrain the typed <c>Equals</c> overload.</typeparam>
[PublicAPI]
public abstract class EqualityBase<T> : IEquatable<T>
    where T : EqualityBase<T>
{
    /// <summary>
    /// Determines whether this instance is equal to <paramref name="other"/> by comparing
    /// their runtime types and <c>EqualityComponents()</c> sequences.
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

        return GetType() == other.GetType() && EqualityComponents().SequenceEqual(other.EqualityComponents());
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

    /// <summary>Computes a hash code from all <c>EqualityComponents()</c> values.</summary>
    /// <returns>A combined hash code consistent with <c>Equals</c>.</returns>
    public sealed override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var component in EqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the ordered sequence of values that define equality for this instance.
    /// Include every field or property that participates in identity; exclude transient or navigation state.
    /// </summary>
    protected abstract IEnumerable<object?> EqualityComponents();
}
