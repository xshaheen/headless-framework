// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>
/// Store-allocated monotonic generation counter for a node id. A higher value always represents a later
/// registration of the same node id, enabling stale-owner detection without clock comparison.
/// </summary>
[PublicAPI]
public readonly record struct NodeIncarnation : IComparable<NodeIncarnation>
{
    /// <summary>Initializes a <see cref="NodeIncarnation"/> with the given generation value.</summary>
    /// <param name="value">The generation counter. Must be a positive (greater than zero) value.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is zero or negative.
    /// </exception>
    public NodeIncarnation(long value)
    {
        Value = Argument.IsPositive(value);
    }

    /// <summary>The underlying generation counter.</summary>
    public long Value { get; }

    /// <inheritdoc/>
    public int CompareTo(NodeIncarnation other)
    {
        return Value.CompareTo(other.Value);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is an earlier generation than <paramref name="right"/>.</summary>
    public static bool operator <(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is the same or an earlier generation than <paramref name="right"/>.</summary>
    public static bool operator <=(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is a later generation than <paramref name="right"/>.</summary>
    public static bool operator >(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="left"/> is the same or a later generation than <paramref name="right"/>.</summary>
    public static bool operator >=(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) >= 0;
    }
}
