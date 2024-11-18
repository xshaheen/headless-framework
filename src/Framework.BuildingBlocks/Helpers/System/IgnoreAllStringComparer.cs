// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.BuildingBlocks.Helpers.System;

/// <summary>
/// Returns string equality only by symbols ignore a case.
/// It can be used for comparing camelCase, PascalCase, snake_case, kebab-case identifiers.
/// </summary>
[PublicAPI]
public sealed class IgnoreAllStringComparer : StringComparer
{
    public static readonly StringComparer Instance = new IgnoreAllStringComparer();

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

    public override bool Equals(string? x, string? y)
    {
        if (x is null || y is null)
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

    public override int GetHashCode(string obj)
    {
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
