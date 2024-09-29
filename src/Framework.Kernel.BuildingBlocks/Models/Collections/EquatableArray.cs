// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Collections;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>An immutable, equatable array. This is equivalent to <see cref="Array"/> but with value equality support.</summary>
/// <typeparam name="T">The type of values in the array.</typeparam>
/// <remarks>Initializes a new instance of the <see cref="EquatableArray{T}"/> struct.</remarks>
/// <param name="array">The input array to wrap.</param>
public readonly struct EquatableArray<T>(T[] array, IEqualityComparer<T>? equalityComparer = null)
    : IEquatable<EquatableArray<T>>,
        IEnumerable<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying <typeparamref name="T"/> array.
    /// </summary>
    private readonly T[] _array = array;

    /// <summary>Gets the length of the array, or 0 if the array is null</summary>
    public int Count => _array?.Length ?? 0;

    /// <summary>Checks whether two <see cref="EquatableArray{T}"/> values are the same.</summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are equal.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    /// <summary>Checks whether two <see cref="EquatableArray{T}"/> values are not the same.</summary>
    /// <param name="left">The first <see cref="EquatableArray{T}"/> value.</param>
    /// <param name="right">The second <see cref="EquatableArray{T}"/> value.</param>
    /// <returns>Whether <paramref name="left"/> and <paramref name="right"/> are not equal.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan(), equalityComparer);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> array && Equals(this, array);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_array is not { } array)
        {
            return 0;
        }

        HashCode hashCode = default;

        if (equalityComparer is null)
        {
            foreach (var item in array)
            {
                hashCode.Add(item);
            }
        }
        else
        {
            foreach (var item in array)
            {
                hashCode.Add(equalityComparer.GetHashCode(item));
            }
        }

        return hashCode.ToHashCode();
    }

    /// <summary>Returns a <see cref="ReadOnlySpan{T}"/> wrapping the current items.</summary>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> wrapping the current items.</returns>
    public ReadOnlySpan<T> AsSpan()
    {
        return _array.AsSpan();
    }

    /// <summary>Returns the underlying wrapped array.</summary>
    /// <returns>Returns the underlying array.</returns>
    public T[] AsArray()
    {
        return _array;
    }

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? [])).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? [])).GetEnumerator();
    }
}
