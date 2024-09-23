#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public abstract class EquatableBase<T> : IEquatable<EquatableBase<T>>
    where T : EquatableBase<T>
{
    public bool Equals(EquatableBase<T>? other)
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

    public static bool operator ==(EquatableBase<T>? left, EquatableBase<T>? right)
    {
        return !(left is null ^ right is null) && left?.Equals(right) != false;
    }

    public static bool operator !=(EquatableBase<T>? left, EquatableBase<T>? right)
    {
        return !(left == right);
    }

    public sealed override bool Equals(object? obj)
    {
        return Equals(obj as EquatableBase<T>);
    }

    public sealed override int GetHashCode()
    {
        return EqualityComponents().Select(x => x?.GetHashCode() ?? 0).Aggregate((x, y) => x ^ y);
    }

    protected abstract IEnumerable<object?> EqualityComponents();
}
