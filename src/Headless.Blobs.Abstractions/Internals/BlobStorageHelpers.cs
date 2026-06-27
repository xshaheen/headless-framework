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

    /// <summary>
    /// Returns a copy of <paramref name="metadata"/> with the framework-internal keys
    /// (<see cref="UploadDateMetadataKey"/>, <see cref="ExtensionMetadataKey"/>) removed, so callers see only the
    /// metadata they supplied. Returns <see langword="null"/> when nothing user-supplied remains. Providers call this
    /// on the metadata they return from <c>GetBlobInfoAsync</c> / <c>OpenReadStreamAsync</c> / listing.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ToUserMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);

        foreach (var pair in metadata)
        {
            if (
                string.Equals(pair.Key, UploadDateMetadataKey, StringComparison.Ordinal)
                || string.Equals(pair.Key, ExtensionMetadataKey, StringComparison.Ordinal)
            )
            {
                continue;
            }

            result[pair.Key] = pair.Value;
        }

        return result.Count == 0 ? null : result;
    }

    [return: NotNullIfNotNull(nameof(path))]
    public static string? NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    /// <summary>
    /// Compiles a glob <paramref name="pattern"/> (<c>*</c> = any run of characters, <c>?</c> = any single character)
    /// into a predicate that tests whole blob keys. This is the single shared client-side matcher layered over
    /// <see cref="IBlobStorage.ListAsync"/> — providers no longer own private glob regex.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>A predicate returning <see langword="true"/> when a key matches <paramref name="pattern"/>.</returns>
    public static Func<string, bool> CreateGlobMatcher(string pattern)
    {
        var regexText = Regex
            .Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        var regex = new Regex($"^{regexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);

        return key => regex.IsMatch(key);
    }

    /// <summary>
    /// Returns the literal (wildcard-free) head of a glob <paramref name="pattern"/> — the substring up to the first
    /// <c>*</c> or <c>?</c>, or the whole pattern when it contains no wildcard. Usable as a server-pushed prefix to
    /// narrow enumeration before the client-side matcher runs.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>The longest non-wildcard prefix of <paramref name="pattern"/>.</returns>
    public static string GetLiteralPrefix(string pattern)
    {
        var wildcardIndex = pattern.IndexOfAny(['*', '?']);

        return wildcardIndex < 0 ? pattern : pattern[..wildcardIndex];
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
