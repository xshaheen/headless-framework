// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Text;

/// <summary>
/// Returns string equality only by symbols ignore a case.
/// It can be used for comparing camelCase, PascalCase, snake_case, kebab-case identifiers.
/// </summary>
[PublicAPI]
public sealed class IgnoreCaseStringComparer : StringComparer
{
    /// <summary>A shared, thread-safe instance of <see cref="IgnoreCaseStringComparer"/>.</summary>
    public static readonly StringComparer Instance = new IgnoreCaseStringComparer();

    /// <summary>
    /// Compares two strings by their letter and digit symbols only (case-insensitive), ignoring any separators such as
    /// spaces, underscores, or dashes. A <see langword="null"/> string sorts before any non-<see langword="null"/> string.
    /// </summary>
    /// <param name="x">The first string to compare.</param>
    /// <param name="y">The second string to compare.</param>
    /// <returns>
    /// A negative value if <paramref name="x"/> precedes <paramref name="y"/>, zero if they are equivalent, or a
    /// positive value if <paramref name="x"/> follows <paramref name="y"/>.
    /// </returns>
    public override int Compare(string? x, string? y)
    {
        if (x is null && y is null)
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int compare;

        int leftIndex = 0,
            rightIndex = 0;

        do
        {
            _GetNextSymbol(x, ref leftIndex, out var leftSymbol);
            _GetNextSymbol(y, ref rightIndex, out var rightSymbol);

            compare = leftSymbol.CompareTo(rightSymbol);
        } while (compare == 0 && leftIndex >= 0 && rightIndex >= 0);

        return compare;
    }

    /// <summary>
    /// Determines whether two strings are equal by their letter and digit symbols only (case-insensitive), ignoring any
    /// separators such as spaces, underscores, or dashes. Two <see langword="null"/> strings are considered equal.
    /// </summary>
    /// <param name="x">The first string to compare.</param>
    /// <param name="y">The second string to compare.</param>
    /// <returns><see langword="true"/> if the strings are equivalent under this comparison; otherwise <see langword="false"/>.</returns>
    public override bool Equals(string? x, string? y)
    {
        if (x is null)
        {
            return y is null;
        }

        if (y is null)
        {
            return false;
        }

        var leftIndex = 0;
        var rightIndex = 0;
        bool equals;

        while (true)
        {
            var hasLeftSymbol = _GetNextSymbol(x, ref leftIndex, out var leftSymbol);
            var hasRightSymbol = _GetNextSymbol(y, ref rightIndex, out var rightSymbol);

            equals = leftSymbol == rightSymbol;

            if (!equals || !hasLeftSymbol || !hasRightSymbol)
            {
                break;
            }
        }

        return equals;
    }

    /// <summary>
    /// Returns a hash code for <paramref name="obj"/> computed from its uppercased letter and digit symbols only, so
    /// that strings considered equal by <see cref="Equals(string?,string?)"/> share the same hash code.
    /// </summary>
    /// <param name="obj">The string to hash. A <see langword="null"/> value yields a hash code of <c>0</c>.</param>
    /// <returns>A hash code based on the symbol content of <paramref name="obj"/>.</returns>
    public override int GetHashCode(string obj)
    {
        if (obj is null)
        {
            return 0;
        }
        unchecked
        {
            int index = 0,
                hash = 0;

            while (_GetNextSymbol(obj, ref index, out var symbol))
            {
                hash = (31 * hash) + char.ToUpperInvariant(symbol).GetHashCode();
            }

            return hash;
        }
    }

    private static bool _GetNextSymbol(string value, ref int startIndex, out char symbol)
    {
        while (startIndex >= 0 && startIndex < value.Length)
        {
            var current = value[startIndex++];

            if (char.IsLetterOrDigit(current))
            {
                symbol = char.ToUpperInvariant(current);

                return true;
            }
        }

        startIndex = -1;
        symbol = char.MinValue;

        return false;
    }
}
