// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.IO;

/// <summary>
/// A helper class for sanitizing untrusted file names and deriving safe display and storage names from them.
/// </summary>
[PublicAPI]
public static class FileNames
{
    #region Invalid FileNames Characters

    /// <summary>
    /// The set of characters that are not allowed in a file name: the quote, angle brackets, pipe, colon, asterisk,
    /// question mark, both path separators (<c>\</c> and <c>/</c>), and all control characters in the range
    /// <c>U+0000</c> through <c>U+001F</c>. Used by <see cref="SanitizeFileName"/>.
    /// </summary>
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
    /// <param name="untrustedName">
    /// The untrusted file name to sanitize. Any extension is preserved and the remainder is cleaned of invalid
    /// characters; path separators are replaced with a space.
    /// </param>
    /// <returns>The sanitized, HTML-encoded file name (including any original extension).</returns>
    public static string SanitizeFileName(ReadOnlySpan<char> untrustedName)
    {
        var extension = Path.GetExtension(untrustedName);
        var stringBuilder = new StringBuilder();
        var spaceCount = 0;

        // First pass: remove invalid chars and normalize spaces (without encoding)
        foreach (var value in untrustedName[..^extension.Length])
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
                stringBuilder.Append(value);
            }
            else if (value is '/' or '\\' or ':')
            {
                // Replace path separators with space (spaceCount is always 0 here since we reset it above)
                stringBuilder.Append(' ');
                spaceCount = 1;
            }
            // else: skip other invalid characters (they're removed)
        }

        var validFileName = string.Concat(stringBuilder.ToString().AsSpan().Trim(), extension);

        // HTML encode the final result to prevent script injection attacks
        return WebUtility.HtmlEncode(validFileName);
    }

    #endregion

    #region Trusted File Name

    /// <summary>
    /// Derives a trusted display name and a unique save name from an untrusted file name, appending a randomly
    /// generated numeric suffix to the save name so it is unlikely to collide with existing files.
    /// </summary>
    /// <param name="untrustedName">The untrusted file name to derive trusted names from.</param>
    /// <returns>
    /// A tuple of <c>TrustedDisplayName</c> (the sanitized name, suitable for display) and <c>UniqueSaveName</c>
    /// (a normalized name with a random suffix, suitable for storing on disk).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="untrustedName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file name cannot be Unicode-normalized.</exception>
    public static (string TrustedDisplayName, string UniqueSaveName) GetTrustedFileName(
        ReadOnlySpan<char> untrustedName
    )
    {
        return GetTrustedFileName(untrustedName, _GetRandomSuffix());
    }

    /// <summary>
    /// Derives a trusted display name and a unique save name from an untrusted file name, appending the supplied
    /// <paramref name="randomSuffix"/> to the save name.
    /// </summary>
    /// <param name="untrustedName">The untrusted file name to derive trusted names from.</param>
    /// <param name="randomSuffix">The suffix to append to the normalized name when building the unique save name.</param>
    /// <returns>
    /// A tuple of <c>TrustedDisplayName</c> (the sanitized name, suitable for display) and <c>UniqueSaveName</c>
    /// (a normalized name with the supplied suffix, suitable for storing on disk).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="untrustedName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the file name cannot be Unicode-normalized.</exception>
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

        // The normalized length is derived from untrusted input, so cap the stack buffer and rent from
        // the shared pool for larger names to avoid a stack-overflow DoS on adversarially long inputs.
        const int maxStackalloc = 256;
        var rentedBuffer = estimatedLength > maxStackalloc ? ArrayPool<char>.Shared.Rent(estimatedLength) : null;

        try
        {
            Span<char> normalizedName = rentedBuffer is not null
                ? rentedBuffer.AsSpan(0, estimatedLength)
                : stackalloc char[estimatedLength];

            if (!fileName.TryNormalize(normalizedName, out var charsWritten, NormalizationForm.FormD))
            {
                throw new InvalidOperationException("Failed to normalize the file name.");
            }

            normalizedName = normalizedName[..charsWritten];

            var builder = new StringBuilder();
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

            var result = builder.ToString().AsSpan();

            // Remove leading ._- characters
            result = result.TrimStart(['.', '_', '-']);
            // Remove trailing ._- characters
            result = result.TrimEnd(['.', '_', '-']);

            return result;
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(rentedBuffer);
            }
        }
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
