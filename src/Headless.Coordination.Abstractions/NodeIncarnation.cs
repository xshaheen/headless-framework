// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Coordination;

/// <summary>Store-allocated monotonic generation for a node id.</summary>
[PublicAPI]
public readonly record struct NodeIncarnation : IComparable<NodeIncarnation>
{
    public NodeIncarnation(long value)
    {
        Value = Argument.IsPositive(value);
    }

    public long Value { get; }

    public int CompareTo(NodeIncarnation other)
    {
        return Value.CompareTo(other.Value);
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}
