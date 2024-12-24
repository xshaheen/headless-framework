// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA1036  // Override methods on comparable types
#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
public sealed class Range<T> : IEquatable<Range<T>>, IComparable<Range<T>>
    where T : IComparable<T>
{
    private Range() { }

    public Range(T? from, T? to)
    {
        if (from is null && to is null)
        {
            throw new InvalidOperationException("At least one of the values must be provided.");
        }

        if (from is not null && to is not null && from.CompareTo(to) > 0)
        {
            throw new InvalidOperationException(
                "Value for `From` must be equal to or greater than the value for `To`."
            );
        }

        From = from;
        To = to;
    }

    public T? From { get; private init; }

    public T? To { get; private init; }

    #region Has Value

    public bool InclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) <= 0) && (To is null || To.CompareTo(value) >= 0);
    }

    public bool ExclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) < 0) && (To is null || To.CompareTo(value) > 0);
    }

    public bool FromInclusiveToExclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) <= 0) && (To is null || To.CompareTo(value) > 0);
    }

    public bool FromExclusiveToInclusiveHas(T? value)
    {
        return value is not null
            && (From is null || From.CompareTo(value) < 0)
            && (To is null || To.CompareTo(value) >= 0);
    }

    #endregion

    #region Has Range

    public bool InclusiveHas(Range<T> range)
    {
        return InclusiveHas(range.From) && InclusiveHas(range.To);
    }

    public bool ExclusiveHas(Range<T> range)
    {
        return ExclusiveHas(range.From) && ExclusiveHas(range.To);
    }

    public bool InRangeLowerInclusive(Range<T> range)
    {
        return FromInclusiveToExclusiveHas(range.From) && FromInclusiveToExclusiveHas(range.To);
    }

    public bool FromExclusiveToInclusiveHas(Range<T> range)
    {
        return FromExclusiveToInclusiveHas(range.From) && FromExclusiveToInclusiveHas(range.To);
    }

    #endregion

    #region Overlap

    public bool IsOverlap(Range<T> other)
    {
        return InclusiveHas(other.From) || InclusiveHas(other.To) || other.InclusiveHas(From) || other.InclusiveHas(To);
    }

    /// <summary>Remove the conflict range parts from the current range.</summary>
    /// <param name="other">The range to remove the conflict parts from the current range.</param>
    /// <param name="addOne"></param>
    /// <param name="subtractOne"></param>
    /// <returns>Returns the remaining parts of the current range after removing the conflict parts.</returns>
    public IEnumerable<Range<T>> RemoveConflictRangeParts(Range<T> other, Func<T, T> addOne, Func<T, T> subtractOne)
    {
        // 1. Same range
        if (Equals(other))
        {
            yield break; // No remaining
        }

        // 2. The other range has the current range inside
        if (other.InclusiveHas(this))
        {
            yield break; // No remaining
        }

        // 3. The current range has the other range inside
        if (InclusiveHas(other))
        {
            if (From is not null && From.CompareTo(other.From) < 0)
            {
                if (other.From is null)
                {
                    yield return new(From, other.From);
                }
                else
                {
                    yield return new(From, subtractOne(other.From));
                }
            }

            if (To is not null && To.CompareTo(other.To) > 0)
            {
                if (other.To is null)
                {
                    yield return new(other.To, To);
                }
                else
                {
                    yield return new(addOne(other.To), To);
                }
            }

            yield break;
        }

        // 4. The current range is before the other range
        if (To is not null && To.CompareTo(other.From) < 0)
        {
            yield return this;
            yield break;
        }

        // 5. The current range is after the other range
        if (From is not null && From.CompareTo(other.To) > 0)
        {
            yield return this;
            yield break;
        }

        // 6. The current range is overlapped with the other range
        if (From is not null && From.CompareTo(other.From) < 0)
        {
            if (other.From is null)
            {
                yield return new(From, other.From);
            }
            else
            {
                yield return new(From, subtractOne(other.From));
            }
        }

        if (To is not null && To.CompareTo(other.To) > 0)
        {
            if (other.To is null)
            {
                yield return new(other.To, To);
            }
            else
            {
                yield return new(addOne(other.To), To);
            }
        }
    }

    #endregion

    #region Creations

    public Range<T> NewTo(T? newEnd)
    {
        return new(From, newEnd);
    }

    public Range<T> NewFrom(T? newStart)
    {
        return new(newStart, To);
    }

    #endregion

    #region Core Overrides

    public override string ToString() => ToString(CultureInfo.InvariantCulture);

    public string ToString(IFormatProvider formatProvider)
    {
        FormattableString format = $"{From}|{To}";

        return format.ToString(formatProvider);
    }

    public void Deconstruct(out T? start, out T? end) => (start, end) = (From, To);

    public int CompareTo(Range<T>? other)
    {
        if (other is null)
        {
            return 1; // By default, it is greater than null.
        }

        if (ReferenceEquals(this, other))
        {
            return 0; // Same reference.
        }

        var fromComparison = From?.CompareTo(other.From) ?? 0;

        return fromComparison != 0 ? fromComparison : To?.CompareTo(other.To) ?? 0;
    }

    public bool Equals(Range<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other)
            || (
                EqualityComparer<T?>.Default.Equals(From, other.From)
                && EqualityComparer<T?>.Default.Equals(To, other.To)
            );
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is Range<T> other && Equals(other));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(From, To);
    }

    public static bool operator <(Range<T>? left, Range<T>? right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    public static bool operator <=(Range<T>? left, Range<T>? right)
    {
        return left is null || left.CompareTo(right) <= 0;
    }

    public static bool operator >(Range<T>? left, Range<T>? right)
    {
        return left?.CompareTo(right) > 0;
    }

    public static bool operator >=(Range<T>? left, Range<T>? right)
    {
        return left is null ? right is null : left.CompareTo(right) >= 0;
    }

    #endregion
}

public static class Range
{
    public static Range<T> Create<T>(T from, T to)
        where T : IComparable<T>
    {
        return new Range<T>(from, to);
    }
}
