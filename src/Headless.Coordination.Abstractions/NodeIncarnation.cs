// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>
/// Represents a store-allocated monotonic generation counter for a node identifier. A higher value represents
/// a later registration of the same node identifier, enabling stale-owner detection without clock synchronization.
/// </summary>
[PublicAPI]
public readonly record struct NodeIncarnation : IComparable<NodeIncarnation>
{
    /// <summary>Initializes a new instance of the <see cref="NodeIncarnation"/> struct.</summary>
    /// <param name="value">The generation counter.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than or equal to zero.</exception>
    public NodeIncarnation(long value)
    {
        Value = Argument.IsPositive(value);
    }

    /// <summary>Gets the underlying generation counter.</summary>
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

    /// <summary>Determines whether <paramref name="left"/> represents an earlier generation than <paramref name="right"/>.</summary>
    /// <param name="left">The first incarnation to compare.</param>
    /// <param name="right">The second incarnation to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is earlier than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>Determines whether <paramref name="left"/> represents an earlier or identical generation compared to <paramref name="right"/>.</summary>
    /// <param name="left">The first incarnation to compare.</param>
    /// <param name="right">The second incarnation to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is earlier than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>Determines whether <paramref name="left"/> represents a later generation than <paramref name="right"/>.</summary>
    /// <param name="left">The first incarnation to compare.</param>
    /// <param name="right">The second incarnation to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is later than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>Determines whether <paramref name="left"/> represents a later or identical generation compared to <paramref name="right"/>.</summary>
    /// <param name="left">The first incarnation to compare.</param>
    /// <param name="right">The second incarnation to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is later than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(NodeIncarnation left, NodeIncarnation right)
    {
        return left.CompareTo(right) >= 0;
    }
}
