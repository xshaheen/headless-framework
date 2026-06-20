// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

[PublicAPI]
public abstract class EqualityBase<T> : IEquatable<EqualityBase<T>>
    where T : EqualityBase<T>
{
    public bool Equals(EqualityBase<T>? other)
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

    public static bool operator ==(EqualityBase<T>? left, EqualityBase<T>? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(EqualityBase<T>? left, EqualityBase<T>? right)
    {
        return !(left == right);
    }

    public sealed override bool Equals(object? obj)
    {
        return Equals(obj as EqualityBase<T>);
    }

    public sealed override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var component in EqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    protected abstract IEnumerable<object?> EqualityComponents();
}
