// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The only shape a client-chosen id may have. A command id is written to the streams of the client's other
/// connections, so anything that could end a line, close a JSON string or split a list is refused here, once, and
/// no transport has to escape an id.
/// </summary>
internal static class LiveToken
{
    public const int MaxLength = 64;

    /// <summary>
    /// Returns whether <paramref name="value" /> is ASCII letters, digits and hyphens only, between
    /// <paramref name="minLength" /> and <see cref="MaxLength" /> characters.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? value, int minLength = 1)
    {
        if (value is null || value.Length < minLength || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
