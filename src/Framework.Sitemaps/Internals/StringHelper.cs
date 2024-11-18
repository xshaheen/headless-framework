// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;

namespace Framework.Sitemaps.Internals;

internal static partial class StringHelper
{
    [GeneratedRegex("[^\t\n\r\u0020-\uD7FF\uE000-\uFFFD]", RegexOptions.None, 100)]
    internal static partial Regex HiddenChars();

    internal static Encoding Utf8WithoutBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Remove control characters from string.</summary>
    internal static string RemoveHiddenChars(this string input)
    {
        return HiddenChars().Replace(input, replacement: string.Empty);
    }
}
