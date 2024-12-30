// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Cysharp.Text;
using Framework.Constants;

namespace Framework.IO;

[PublicAPI]
public static partial class FileNameHelper
{
    #region Invalid FileNames Characters

    public static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create(
        '"',
        '<',
        '>',
        '|',
        char.MinValue,
        '\u0001',
        '\u0002',
        '\u0003',
        '\u0004',
        '\u0005',
        '\u0006',
        '\a',
        '\b',
        '\t',
        '\n',
        '\v',
        '\f',
        '\r',
        '\u000E',
        '\u000F',
        '\u0010',
        '\u0011',
        '\u0012',
        '\u0013',
        '\u0014',
        '\u0015',
        '\u0016',
        '\u0017',
        '\u0018',
        '\u0019',
        '\u001A',
        '\u001B',
        '\u001C',
        '\u001D',
        '\u001E',
        '\u001F',
        ':',
        '*',
        '?',
        '\\',
        '/'
    );

    #endregion

    #region Trusted File Name

    public static (string TrusedDisplayName, string UniqueSaveName) GetTrustedFileNames(
        ReadOnlySpan<char> untrustedBlobName
    )
    {
        var untrustedFileName = Path.GetFileName(untrustedBlobName);
        var extension = Path.GetExtension(untrustedFileName);

        var sanitizedFileName = SanitizeFileName(untrustedFileName);
        var trustedDisplayName = sanitizedFileName + extension.ToString();

        var normalizeFileName = _NormalizeFileName(sanitizedFileName);
        var randomNumber = RandomNumberGenerator.GetInt32(10_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var uniqueSaveName = normalizeFileName + "_" + randomNumber + extension.ToString();

        return (trustedDisplayName, uniqueSaveName);
    }

    /// <summary>
    /// Normalize file name by:
    /// <list type="bullet">
    /// <item>Replace all spaces with underscore</item>
    /// <item>Normalize accent characters</item>
    /// <item>Replace all symbol characters with dash</item>
    /// <item>Replace all duplicated ._- characters with underscore</item>
    /// <item>Remove all un-allowed ._- postfix characters</item>
    /// <item>Remove all un-allowed ._- suffix characters</item>
    /// </list>
    /// </summary>
    private static string _NormalizeFileName(string fileName)
    {
        var result = RegexPatterns
            .Spaces.Replace(fileName, "_")
            ._RemoveAccentCharacters()
            ._ReplaceSymbolCharacters()
            ._JoinAsString()
            ._ReplaceUnAllowedDuplicatedCharacters()
            ._ReplaceUnAllowedSuffixCharacters()
            ._ReplaceUnAllowedPostfixCharacters();

        return result;
    }

    private static IEnumerable<char> _RemoveAccentCharacters(this string input)
    {
        return from c in input.Normalize(NormalizationForm.FormD) where _IsLetterOrDigitOrSpace(c) select c;
    }

    private static IEnumerable<char> _ReplaceSymbolCharacters(this IEnumerable<char> input)
    {
        return input.Select(ch => ch != '_' && (char.IsPunctuation(ch) || char.IsSymbol(ch)) ? '-' : ch);
    }

    private static string _ReplaceUnAllowedDuplicatedCharacters(this string input)
    {
        return _DuplicatedPattern().Replace(input, "_");
    }

    private static string _ReplaceUnAllowedPostfixCharacters(this string input)
    {
        return _PostfixPattern().Replace(input, "");
    }

    private static string _ReplaceUnAllowedSuffixCharacters(this string input)
    {
        return _SuffixPattern().Replace(input, "");
    }

    private static string _JoinAsString(this IEnumerable<char> chars)
    {
        return string.Join("", chars);
    }

    private static bool _IsLetterOrDigitOrSpace(char c)
    {
        return CharUnicodeInfo.GetUnicodeCategory(c)
            is UnicodeCategory.LowercaseLetter
                or UnicodeCategory.UppercaseLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.DecimalDigitNumber;
    }

    [GeneratedRegex("[._-]{2,}", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _DuplicatedPattern();

    [GeneratedRegex(".*[._-]+$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _PostfixPattern();

    [GeneratedRegex("^[._-]+", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _SuffixPattern();

    #endregion

    #region Sanitize File Name

    /// <summary>
    /// Sanitizes the given file name by removing invalid characters and replacing multiple spaces with a single space
    /// and encoded the result to HTML to prevent script injection attacks.
    /// </summary>
    /// <param name="untrustedName">The file name without extension to sanitize.</param>
    public static string SanitizeFileName(ReadOnlySpan<char> untrustedName)
    {
        var validFileName = ZString.CreateStringBuilder();

        var spaceCount = 0;

        foreach (var value in untrustedName)
        {
            if (InvalidFileNameChars.Contains(value))
            {
                continue;
            }

            if (value == ' ')
            {
                spaceCount++;
            }
            else
            {
                spaceCount = 0;
            }

            if (spaceCount > 1)
            {
                continue;
            }

            validFileName.Append(value);
        }

        var htmlEncoded = WebUtility.HtmlEncode(validFileName.ToString());

        return htmlEncoded;
    }

    #endregion
}
