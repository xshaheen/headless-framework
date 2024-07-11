namespace Framework.BuildingBlocks.Domains;

public abstract class Base<T> : IEquatable<Base<T>>
    where T : Base<T>
{
    public bool Equals(Base<T>? other)
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

    public static bool operator ==(Base<T>? left, Base<T>? right)
    {
        return !(left is null ^ right is null) && left?.Equals(right) != false;
    }

    public static bool operator !=(Base<T>? left, Base<T>? right)
    {
        return !(left == right);
    }

    public sealed override bool Equals(object? obj)
    {
        return Equals(obj as Base<T>);
    }

    public sealed override int GetHashCode()
    {
        return EqualityComponents().Select(x => x?.GetHashCode() ?? 0).Aggregate((x, y) => x ^ y);
    }

    protected abstract IEnumerable<object?> EqualityComponents();
}
