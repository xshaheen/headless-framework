// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Recaptcha.Internals;

internal static class StringExtensions
{
    public static string? RemovePostFix(
        this string str,
        StringComparison comparisonType,
        params ReadOnlySpan<string> postFixes
    )
    {
        if (string.IsNullOrEmpty(str))
        {
            return null;
        }

        if (postFixes.IsEmpty)
        {
            return str;
        }

        foreach (var postFix in postFixes)
        {
            if (str.EndsWith(postFix, comparisonType))
            {
                return str.Left(str.Length - postFix.Length);
            }
        }

        return str;
    }

    public static string Left(this string str, int len)
    {
        return str.Length < len ? str : str[..len];
    }
}
