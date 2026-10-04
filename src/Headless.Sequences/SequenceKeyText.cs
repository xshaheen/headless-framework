// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sequences;

/// <summary>Provides validation utilities for portable sequence key components across database providers.</summary>
internal static class SequenceKeyText
{
    /// <summary>
    /// Checks whether all database providers store, compare, and round-trip <paramref name="value" /> without alteration.
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
