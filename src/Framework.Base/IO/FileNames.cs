// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using Cysharp.Text;
using Framework.Checks;

namespace Framework.IO;

[PublicAPI]
public static class FileNames
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

    #region Sanitize File Name

    /// <summary>
    /// Sanitizes the given file name by removing invalid characters and replacing multiple spaces with a single space
    /// and encoded the result to HTML to prevent script injection attacks.
    /// </summary>
    /// <param name="untrustedName">The file name without extension to sanitize.</param>
    public static string SanitizeFileName(ReadOnlySpan<char> untrustedName)
    {
        var extension = Path.GetExtension(untrustedName);
        var stringBuilder = ZString.CreateStringBuilder();
        var spaceCount = 0;

        foreach (var value in WebUtility.HtmlEncode(untrustedName[..^extension.Length].ToString()))
        {
            // The next part ensures that multiple consecutive spaces are reduced to a single space
            var isWhiteSpace = char.IsWhiteSpace(value);

            if (isWhiteSpace)
            {
                if (spaceCount == 0)
                {
                    stringBuilder.Append(' ');
                }

                spaceCount++;
                continue;
            }

            spaceCount = 0;

            if (!InvalidFileNameChars.Contains(value))
            {
                stringBuilder.Append(char.ToLowerInvariant(value));
            }
            else
            {
                // Replace invalid characters with a space
                stringBuilder.Append(' ');
                spaceCount++;
            }
        }

        var validFileName = string.Concat(stringBuilder.AsSpan().Trim(), extension);
        var encodedFileName = WebUtility.HtmlEncode(validFileName);

        return encodedFileName;
    }

    #endregion

    #region Trusted File Name

    public static (string TrustedDisplayName, string UniqueSaveName) GetTrustedFileName(
        ReadOnlySpan<char> untrustedName
    )
    {
        return GetTrustedFileName(untrustedName, _GetRandomSuffix());
    }

    public static (string TrustedDisplayName, string UniqueSaveName) GetTrustedFileName(
        ReadOnlySpan<char> untrustedName,
        ReadOnlySpan<char> randomSuffix
    )
    {
        Argument.IsNotEmpty(untrustedName);

        var extension = Path.GetExtension(untrustedName); // includes the dot, e.g., ".png"
        var sanitizedName = SanitizeFileName(untrustedName[..^extension.Length]);

        // Trusted display name (sanitized but without random suffix)
        var trustedDisplayName = string.Concat(sanitizedName, extension);

        // Normalize for unique save name (with random suffix)
        var normalizeFileName = _NormalizeFileName(sanitizedName);
        var uniqueSaveName = string.Concat(normalizeFileName, randomSuffix, extension);

        return (trustedDisplayName, uniqueSaveName);
    }

    private static ReadOnlySpan<char> _NormalizeFileName(ReadOnlySpan<char> fileName)
    {
        if (fileName.IsEmpty)
        {
            return fileName;
        }

        var estimatedLength = fileName.GetNormalizedLength(NormalizationForm.FormD);
        Span<char> normalizedName = stackalloc char[estimatedLength];

        if (!fileName.TryNormalize(normalizedName, out var charsWritten, NormalizationForm.FormD))
        {
            throw new InvalidOperationException("Failed to normalize the file name.");
        }

        normalizedName = normalizedName[..charsWritten];

        var builder = ZString.CreateStringBuilder();
        var lastChar = '\0';

        foreach (var c in normalizedName) // Normalize accent characters
        {
            char charToAppend;

            // Replace all spaces with underscore (it should not contain duplicate spaces because SanitizeFileName does that)
            // Replace all symbol characters with dash
            if (char.IsWhiteSpace(c) || c == '_' || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                charToAppend = '_';
            }
            else if (_IsLetterOrDigit(c)) // Keep letters and digits
            {
                charToAppend = c;
            }
            else
            {
                continue; // Skip this character
            }

            // Skip duplicated [._-] characters
            if ((charToAppend is '.' or '_' or '-') && (lastChar is '.' or '_' or '-'))
            {
                continue;
            }

            builder.Append(charToAppend);
            lastChar = charToAppend;
        }

        var result = builder.AsSpan();

        // Remove leading ._- characters
        result = result.TrimStart(['.', '_', '-']);
        // Remove trailing ._- characters
        result = result.TrimEnd(['.', '_', '-']);

        return result;
    }

    private static ReadOnlySpan<char> _GetRandomSuffix()
    {
        var randomNumber = RandomNumberGenerator
            .GetInt32(1_000_000, int.MaxValue)
            .ToString(CultureInfo.InvariantCulture);

        return "_" + randomNumber;
    }

    private static bool _IsLetterOrDigit(char c)
    {
        return CharUnicodeInfo.GetUnicodeCategory(c)
            is UnicodeCategory.LowercaseLetter
                or UnicodeCategory.UppercaseLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber;
    }

    #endregion
}
