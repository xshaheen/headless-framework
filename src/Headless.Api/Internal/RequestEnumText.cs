// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// The rule MVC and Minimal API binding share for an enum outside the body: a number never binds, because the JSON body
/// refuses one and an enum's numbers are not a contract a client should depend on.
/// </summary>
internal static class RequestEnumText
{
    /// <summary>
    /// Whether <paramref name="text"/> carries a number: a part that starts with a digit or a sign. Each comma-separated
    /// part is checked because MVC's enum converter reads a number inside a <c>[Flags]</c> list (<c>Read,2</c> binds
    /// <c>Read, Write</c>), although <c>Enum.TryParse</c> refuses one. Reads the text in place.
    /// </summary>
    public static bool IsNumeric(string text)
    {
        foreach (var range in text.AsSpan().Split(','))
        {
            var part = text.AsSpan()[range].TrimStart();

            if (part.Length > 0 && (char.IsAsciiDigit(part[0]) || part[0] is '-' or '+'))
            {
                return true;
            }
        }

        return false;
    }
}
