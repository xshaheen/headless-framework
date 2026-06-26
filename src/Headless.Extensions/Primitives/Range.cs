// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Represents an inclusive interval bounded by an optional lower bound (<see cref="From"/>) and an optional upper
/// bound (<see cref="To"/>). A <see langword="null"/> bound means the interval is unbounded on that side.
/// </summary>
/// <typeparam name="T">The comparable element type of the interval bounds.</typeparam>
[PublicAPI]
#pragma warning disable CA1036  // Override methods on comparable types
public sealed class Range<T> : IEquatable<Range<T>>, IComparable<Range<T>>
    where T : IComparable<T>
{
    private Range() { }

    /// <summary>Initializes a new <see cref="Range{T}"/> with the given (optional) lower and upper bounds.</summary>
    /// <param name="from">The lower bound, or <see langword="null"/> for an interval unbounded below.</param>
    /// <param name="to">The upper bound, or <see langword="null"/> for an interval unbounded above.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both <paramref name="from"/> and <paramref name="to"/> are <see langword="null"/>, or when
    /// <paramref name="from"/> is greater than <paramref name="to"/>.
    /// </exception>
    public Range(T? from, T? to)
    {
        if (from is null && to is null)
        {
            throw new InvalidOperationException("At least one of the values must be provided.");
        }

        if (from is not null && to is not null && from.CompareTo(to) > 0)
        {
            throw new InvalidOperationException("Value for `From` must be equal to or less than the value for `To`.");
        }

        From = from;
        To = to;
    }

    /// <summary>The lower bound of the interval, or <see langword="null"/> when unbounded below.</summary>
    public T? From { get; private init; }

    /// <summary>The upper bound of the interval, or <see langword="null"/> when unbounded above.</summary>
    public T? To { get; private init; }

    #region Has Value

    /// <summary>Determines whether <paramref name="value"/> lies within the interval, treating both bounds as inclusive.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is within <c>[From, To]</c>; otherwise, <see langword="false"/>.</returns>
    public bool InclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) <= 0) && (To is null || To.CompareTo(value) >= 0);
    }

    /// <summary>Determines whether <paramref name="value"/> lies within the interval, treating both bounds as exclusive.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is within <c>(From, To)</c>; otherwise, <see langword="false"/>.</returns>
    public bool ExclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) < 0) && (To is null || To.CompareTo(value) > 0);
    }

    /// <summary>Determines whether <paramref name="value"/> lies within the interval with an inclusive lower bound and exclusive upper bound.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is within <c>[From, To)</c>; otherwise, <see langword="false"/>.</returns>
    public bool FromInclusiveToExclusiveHas(T? value)
    {
        return (From is null || From.CompareTo(value) <= 0) && (To is null || To.CompareTo(value) > 0);
    }

    /// <summary>Determines whether <paramref name="value"/> lies within the interval with an exclusive lower bound and inclusive upper bound.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is non-null and within <c>(From, To]</c>; otherwise, <see langword="false"/>.</returns>
    public bool FromExclusiveToInclusiveHas(T? value)
    {
        return value is not null
            && (From is null || From.CompareTo(value) < 0)
            && (To is null || To.CompareTo(value) >= 0);
    }

    #endregion

    #region Has Range

    /// <summary>Determines whether <paramref name="range"/> is fully contained within this interval, with both bounds inclusive.</summary>
    /// <param name="range">The range to test for containment.</param>
    /// <returns><see langword="true"/> if both bounds of <paramref name="range"/> lie within <c>[From, To]</c>; otherwise, <see langword="false"/>.</returns>
    public bool InclusiveHas(Range<T> range)
    {
        return InclusiveHas(range.From) && InclusiveHas(range.To);
    }

    /// <summary>Determines whether <paramref name="range"/> is fully contained within this interval, with both bounds exclusive.</summary>
    /// <param name="range">The range to test for containment.</param>
    /// <returns><see langword="true"/> if both bounds of <paramref name="range"/> lie within <c>(From, To)</c>; otherwise, <see langword="false"/>.</returns>
    public bool ExclusiveHas(Range<T> range)
    {
        return ExclusiveHas(range.From) && ExclusiveHas(range.To);
    }

    /// <summary>Determines whether <paramref name="range"/> lies within this interval using inclusive-lower, exclusive-upper containment for both of its bounds.</summary>
    /// <param name="range">The range to test for containment.</param>
    /// <returns><see langword="true"/> if both bounds of <paramref name="range"/> lie within <c>[From, To)</c>; otherwise, <see langword="false"/>.</returns>
    public bool InRangeLowerInclusive(Range<T> range)
    {
        return FromInclusiveToExclusiveHas(range.From) && FromInclusiveToExclusiveHas(range.To);
    }

    /// <summary>Determines whether <paramref name="range"/> lies within this interval using exclusive-lower, inclusive-upper containment for both of its bounds.</summary>
    /// <param name="range">The range to test for containment.</param>
    /// <returns><see langword="true"/> if both bounds of <paramref name="range"/> lie within <c>(From, To]</c>; otherwise, <see langword="false"/>.</returns>
    public bool FromExclusiveToInclusiveHas(Range<T> range)
    {
        return FromExclusiveToInclusiveHas(range.From) && FromExclusiveToInclusiveHas(range.To);
    }

    #endregion

    #region Overlap

    /// <summary>Determines whether this interval overlaps <paramref name="other"/> using inclusive bounds.</summary>
    /// <param name="other">The range to test for overlap.</param>
    /// <returns><see langword="true"/> if the intervals share at least one point; otherwise, <see langword="false"/>.</returns>
    public bool IsOverlap(Range<T> other)
    {
        return InclusiveHas(other.From) || InclusiveHas(other.To) || other.InclusiveHas(From) || other.InclusiveHas(To);
    }

    /// <summary>Returns the sub-ranges of this interval that remain after subtracting the overlapping portion with <paramref name="other"/>.</summary>
    /// <param name="other">The range whose overlapping portion is removed from this range.</param>
    /// <param name="addOne">A function returning the successor of a bound, used to start a remaining part just past <paramref name="other"/>'s upper bound.</param>
    /// <param name="subtractOne">A function returning the predecessor of a bound, used to end a remaining part just before <paramref name="other"/>'s lower bound.</param>
    /// <returns>The non-overlapping sub-ranges of this range; an empty sequence when this range is fully covered by <paramref name="other"/>.</returns>
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

    /// <summary>Creates a new range with the same lower bound and a different upper bound.</summary>
    /// <param name="newEnd">The new upper bound, or <see langword="null"/> for unbounded above.</param>
    /// <returns>A new <see cref="Range{T}"/> spanning <see cref="From"/> to <paramref name="newEnd"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both bounds would be <see langword="null"/>, or when <see cref="From"/> is greater than <paramref name="newEnd"/>.
    /// </exception>
    public Range<T> NewTo(T? newEnd)
    {
        return new(From, newEnd);
    }

    /// <summary>Creates a new range with a different lower bound and the same upper bound.</summary>
    /// <param name="newStart">The new lower bound, or <see langword="null"/> for unbounded below.</param>
    /// <returns>A new <see cref="Range{T}"/> spanning <paramref name="newStart"/> to <see cref="To"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both bounds would be <see langword="null"/>, or when <paramref name="newStart"/> is greater than <see cref="To"/>.
    /// </exception>
    public Range<T> NewFrom(T? newStart)
    {
        return new(newStart, To);
    }

    #endregion

    #region Core Overrides

    /// <summary>Returns the range formatted as <c>{From}|{To}</c> using the invariant culture.</summary>
    public override string ToString() => ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns the range formatted as <c>{From}|{To}</c> using the given format provider.</summary>
    /// <param name="formatProvider">The format provider used to format the bounds.</param>
    /// <returns>The formatted range string.</returns>
    public string ToString(IFormatProvider formatProvider)
    {
        // string.Create formats the interpolated handler directly with the provider, avoiding the FormattableString allocation.
        return string.Create(formatProvider, $"{From}|{To}");
    }

    /// <summary>Deconstructs the range into its lower and upper bounds.</summary>
    /// <param name="start">Receives the <see cref="From"/> bound.</param>
    /// <param name="end">Receives the <see cref="To"/> bound.</param>
    public void Deconstruct(out T? start, out T? end) => (start, end) = (From, To);

    /// <summary>Compares this range to <paramref name="other"/>, ordering by lower bound and then by upper bound.</summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns>
    /// A negative value if this range precedes <paramref name="other"/>, zero if they are equivalent, and a positive
    /// value if this range follows <paramref name="other"/> (a <see langword="null"/> <paramref name="other"/> is treated as smaller).
    /// </returns>
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

        // Lower bound: an unbounded-below (`null`) `From` sorts before any concrete lower bound. Comparing the
        // raw nullables would treat a null `From` as equal to every `other.From`, which contradicts Equals.
        int fromComparison;

        if (From is null)
        {
            fromComparison = other.From is null ? 0 : -1;
        }
        else if (other.From is null)
        {
            fromComparison = 1;
        }
        else
        {
            fromComparison = From.CompareTo(other.From);
        }

        if (fromComparison != 0)
        {
            return fromComparison;
        }

        // Upper bound: an unbounded-above (`null`) `To` sorts after any concrete upper bound.
        if (To is null)
        {
            return other.To is null ? 0 : 1;
        }

        if (other.To is null)
        {
            return -1;
        }

        return To.CompareTo(other.To);
    }

    /// <summary>Determines whether this range equals <paramref name="other"/> by both bounds.</summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns><see langword="true"/> if both ranges have equal bounds; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>Determines whether <paramref name="obj"/> is a <see cref="Range{T}"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal range; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is Range<T> other && Equals(other));
    }

    /// <summary>Returns a hash code derived from the lower and upper bounds.</summary>
    /// <returns>A hash code for the current range.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(From, To);
    }

    /// <summary>Determines whether <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The first range to compare.</param>
    /// <param name="right">The second range to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(Range<T>? left, Range<T>? right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    /// <summary>Determines whether <paramref name="left"/> sorts before or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first range to compare.</param>
    /// <param name="right">The second range to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(Range<T>? left, Range<T>? right)
    {
        return left is null || left.CompareTo(right) <= 0;
    }

    /// <summary>Determines whether <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The first range to compare.</param>
    /// <param name="right">The second range to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(Range<T>? left, Range<T>? right)
    {
        return left?.CompareTo(right) > 0;
    }

    /// <summary>Determines whether <paramref name="left"/> sorts after or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first range to compare.</param>
    /// <param name="right">The second range to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(Range<T>? left, Range<T>? right)
    {
        return left is null ? right is null : left.CompareTo(right) >= 0;
    }

    #endregion
}

/// <summary>Factory helpers for creating <see cref="Range{T}"/> instances.</summary>
public static class Range
{
    /// <summary>Creates a <see cref="Range{T}"/> from a lower and upper bound.</summary>
    /// <typeparam name="T">The comparable element type of the interval bounds.</typeparam>
    /// <param name="from">The lower bound.</param>
    /// <param name="to">The upper bound.</param>
    /// <returns>A new <see cref="Range{T}"/> spanning <paramref name="from"/> to <paramref name="to"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="from"/> is greater than <paramref name="to"/>.</exception>
    public static Range<T> Create<T>(T from, T to)
        where T : IComparable<T>
    {
        return new Range<T>(from, to);
    }
}
