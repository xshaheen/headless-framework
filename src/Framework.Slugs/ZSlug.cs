// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;

namespace Framework.Slugs;

public static class ZSlug
{
    public static string Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var span = input.Normalize(NormalizationForm.FormD).AsSpan();

        var builder = ZString.CreateStringBuilder();
        var hasPreviousDash = false;

        foreach (var c in span)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            switch (c)
            {
                case '&':
                    if (!hasPreviousDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        hasPreviousDash = true;
                    }
                    builder.Append("and-");

                    continue;
                case '.':
                    if (!hasPreviousDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        hasPreviousDash = true;
                    }
                    builder.Append("dot-");

                    continue;
                case '+':
                    if (!hasPreviousDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        hasPreviousDash = true;
                    }
                    builder.Append("plus-");

                    continue;
                case '%':
                    if (!hasPreviousDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        hasPreviousDash = true;
                    }
                    builder.Append("percent-");

                    continue;
            }

            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                if (!hasPreviousDash && builder.Length > 0)
                {
                    builder.Append('-');
                    hasPreviousDash = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            hasPreviousDash = false;
        }

        if (builder.AsSpan()[^1] == '-')
        {
            builder.Remove(builder.Length - 1, 1);
        }

        return builder.ToString();
    }
}
