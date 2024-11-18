// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Framework.BuildingBlocks.Helpers.System;
using Humanizer;
using Polly;
using Polly.Retry;
using File = System.IO.File;

namespace Framework.BuildingBlocks.Helpers.IO;

[PublicAPI]
public static partial class FileHelper
{
    #region Invalid FileNames Characters

    public static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create(
        [
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
            '/',
        ]
    );

    #endregion

    #region Pipeline

    public static readonly RetryStrategyOptions IoRetryStrategyOptions =
        new()
        {
            Name = "BlobToLocalFileRetryPolicy",
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            Delay = 0.5.Seconds(),
            ShouldHandle = new PredicateBuilder().Handle<IOException>(),
        };

    public static readonly ResiliencePipeline IoRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(IoRetryStrategyOptions)
        .Build();

    #endregion

    #region Delete If Exists

    /// <summary>Checks and deletes given a file if it does exist.</summary>
    /// <param name="filePath">Path of the file</param>
    public static bool DeleteIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);

        return true;
    }

    #endregion

    #region Read Content

    /// <summary>Opens a text file, reads content without BOM.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string?> ReadFileWithoutBomAsync(string path)
    {
        var bytes = await ReadAllBytesAsync(path);

        return StringHelper.ConvertFromBytesWithoutBom(bytes);
    }

    /// <summary>
    /// Opens a text file, reads all lines of the file, and then closes the file.
    /// </summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        using var reader = File.OpenText(path);

        return await reader.ReadToEndAsync();
    }

    /// <summary>Opens a text file, reads all lines of the file, and then closes the file.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<byte[]> ReadAllBytesAsync(string path)
    {
        await using var stream = File.Open(path, FileMode.Open);

        var result = new byte[stream.Length];
        _ = await stream.ReadAsync(result.AsMemory(0, (int)stream.Length));

        return result;
    }

    /// <summary>Opens a text file, reads all lines of the file, and then closes the file.</summary>
    /// <param name="path">The file to open for reading.</param>
    /// <param name="encoding">Encoding of the file. Default is UTF8</param>
    /// <param name="fileMode">Specifies how the operating system should open a file. Default is Open</param>
    /// <param name="fileAccess">
    /// Defines constants for read, write, or read/write access to a file. Default
    /// is Read
    /// </param>
    /// <param name="fileShare">
    /// Contains constants for controlling the kind of access other FileStream objects can have to the
    /// same file. Default is Read
    /// </param>
    /// <param name="bufferSize">Length of StreamReader buffer. Default is 4096.</param>
    /// <param name="fileOptions">
    /// Indicates FileStream options. Default is Asynchronous (The file is to be used for
    /// asynchronous reading.) and SequentialScan (The file is to be accessed sequentially from beginning
    /// to end.)
    /// </param>
    /// <returns>A string containing all lines of the file.</returns>
    public static async Task<string[]> ReadAllLinesAsync(
        string path,
        Encoding? encoding = null,
        FileMode fileMode = FileMode.Open,
        FileAccess fileAccess = FileAccess.Read,
        FileShare fileShare = FileShare.Read,
        int bufferSize = 4096,
        FileOptions fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan
    )
    {
        encoding ??= Encoding.UTF8;

        var lines = new List<string>();

        await using (var stream = new FileStream(path, fileMode, fileAccess, fileShare, bufferSize, fileOptions))
        using (var reader = new StreamReader(stream, encoding))
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
            }
        }

        return lines.AsArray();
    }

    #endregion

    #region Sanitize File Name

    public static (string TrusedDisplayName, string UniqueSaveName) GetTrustedFileNames(
        ReadOnlySpan<char> untrustedBlobName
    )
    {
        var untrustedFileName = Path.GetFileName(untrustedBlobName);
        var extension = Path.GetExtension(untrustedFileName);

        var sanitizedFileName = SanitizeFileName(untrustedFileName);
        var normalizeFileName = _NormalizeFileName(sanitizedFileName);
        var randomNumber = RandomNumberGenerator.GetInt32(10_000, int.MaxValue);

        var trusedDisplayName = sanitizedFileName + extension.ToString();
        var uniqueSaveName =
            normalizeFileName + "_" + randomNumber.ToString(CultureInfo.InvariantCulture) + extension.ToString();

        return (trusedDisplayName, uniqueSaveName);
    }

    /// <summary>
    /// Sanitizes the given file name by removing invalid characters and replacing multiple spaces with a single space
    /// and encoded the result to HTML to prevent script injection attacks.
    /// </summary>
    /// <param name="untrustedName">The file name without extension to sanitize.</param>
    public static string SanitizeFileName(ReadOnlySpan<char> untrustedName)
    {
        var spaceCount = 0;
        var validFileName = new StringBuilder(untrustedName.Length);

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
            .Spaces()
            .Replace(fileName, "_")
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
        return from ch in input.Normalize(NormalizationForm.FormD)
            let category = CharUnicodeInfo.GetUnicodeCategory(ch)
            where category != UnicodeCategory.NonSpacingMark
            select ch;
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

    [GeneratedRegex("[._-]{2,}", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _DuplicatedPattern();

    [GeneratedRegex(".*[._-]+$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _PostfixPattern();

    [GeneratedRegex("^[._-]+", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _SuffixPattern();

    #endregion
}
