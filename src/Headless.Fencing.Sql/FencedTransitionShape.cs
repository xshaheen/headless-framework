// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// Everything a rendered fenced transition depends on besides its table: two calls with equal shapes render the same
/// statement. Fragments compare by value, so a write rebuilt per call with the same fragments finds the same statement.
/// </summary>
internal readonly struct FencedTransitionShape(
    FencedTransitionKind kind,
    string? set,
    string? condition,
    IReadOnlyList<string>? returning
) : IEquatable<FencedTransitionShape>
{
    public FencedTransitionKind Kind { get; } = kind;

    public string? Set { get; } = set;

    public string? Condition { get; } = condition;

    public IReadOnlyList<string>? Returning { get; } = returning;

    /// <summary>
    /// A copy whose column list the caller can no longer change, so a key held by the cache keeps its hash.
    /// </summary>
    public FencedTransitionShape Detached()
    {
        return Returning is null ? this : new FencedTransitionShape(Kind, Set, Condition, [.. Returning]);
    }

    public bool Equals(FencedTransitionShape other)
    {
        return Kind == other.Kind
            && string.Equals(Set, other.Set, StringComparison.Ordinal)
            && string.Equals(Condition, other.Condition, StringComparison.Ordinal)
            && _SameColumns(Returning, other.Returning);
    }

    public override bool Equals(object? obj) => obj is FencedTransitionShape other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Set, StringComparer.Ordinal);
        hash.Add(Condition, StringComparer.Ordinal);

        if (Returning is { } returning)
        {
            hash.Add(returning.Count);

            for (var i = 0; i < returning.Count; i++)
            {
                hash.Add(returning[i], StringComparer.Ordinal);
            }
        }
        else
        {
            hash.Add(-1);
        }

        return hash.ToHashCode();
    }

    private static bool _SameColumns(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
