// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Constants;
using Headless.Urls;

namespace Headless.Blobs.Internals;

/// <summary>
/// Shared helpers for blob storage providers.
/// </summary>
public static class BlobStorageHelpers
{
    public const string UploadDateMetadataKey = "uploadDate";
    public const string ExtensionMetadataKey = "extension";

    /// <summary>
    /// Reserved object-key suffix used by filesystem-like providers (file system, SFTP) to store a blob's metadata in
    /// a companion ("sidecar") file next to its content. Blob keys ending in this suffix are rejected at
    /// <see cref="BlobLocation"/> construction so user blobs can never collide with a sidecar.
    /// </summary>
    public const string SidecarSuffix = ".hlmeta";

    /// <summary>Returns <see langword="true"/> when <paramref name="key"/> is reserved for sidecar metadata (ends with <see cref="SidecarSuffix"/>).</summary>
    public static bool IsSidecarKey(string key) => key.EndsWith(SidecarSuffix, StringComparison.Ordinal);

    [return: NotNullIfNotNull(nameof(path))]
    public static string? NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    public static SearchCriteria GetRequestCriteria(IEnumerable<string> directories, string? searchPattern)
    {
        searchPattern = Url.Combine(string.Join('/', directories), NormalizePath(searchPattern));

        if (string.IsNullOrEmpty(searchPattern))
        {
            return new();
        }

        var hasWildcard = searchPattern.Contains('*', StringComparison.Ordinal);

        var prefix = searchPattern;
        Regex? patternRegex = null;

        if (hasWildcard)
        {
            var searchRegexText = Regex.Escape(searchPattern).Replace("\\*", ".*?", StringComparison.Ordinal);
            patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);

            var slashPos = searchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? searchPattern[..(slashPos + 1)] : string.Empty;
        }

        return new(prefix, patternRegex);
    }
}

public sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);
