// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sequences;

/// <summary>Text rules every key part (tenant id, name, partition) follows on every provider.</summary>
internal static class SequenceKeyText
{
    /// <summary>
    /// Whether every provider stores, compares, and returns <paramref name="value" /> unchanged, for validators that
    /// report instead of throw. Call validation uses <see cref="Argument.IsPortableKey" /> directly.
    /// </summary>
    public static bool IsPortable(string? value)
    {
        try
        {
            Argument.IsPortableKey(value);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
