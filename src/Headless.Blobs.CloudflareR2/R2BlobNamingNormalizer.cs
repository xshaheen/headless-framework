// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internals;
using Headless.Core;

namespace Headless.Blobs.CloudflareR2;

/// <summary>
/// Normalizes container names to Cloudflare R2 bucket rules: 3–63 characters, lowercase letters, digits, and
/// hyphens only. Unlike Amazon S3, R2 bucket names may not contain dots.
/// </summary>
public sealed partial class R2BlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary><a href="https://developers.cloudflare.com/r2/buckets/" /></summary>
    public string NormalizeContainerName(string containerName)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            containerName = containerName.ToLowerInvariant();

            // R2 bucket names allow only lowercase letters, digits, and hyphens (no dots).
            containerName = _NotAllowedCharactersRegex().Replace(containerName, string.Empty);
            containerName = _LeadingHyphensRegex().Replace(containerName, string.Empty);

            // Enforce the 63-char ceiling after normalization, then strip any hyphen the truncation may expose.
            if (containerName.Length > 63)
            {
                containerName = containerName[..63];
            }

            containerName = _TrailingHyphensRegex().Replace(containerName, string.Empty);

            // R2 bucket names must be at least 3 characters.
            return containerName.Length >= 3 ? containerName : containerName.PadRight(3, '0');
        }
    }

    /// <summary>Validates <paramref name="blobName"/> and returns it unchanged.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> contains path-traversal sequences or control characters.</exception>
    public string NormalizeBlobName(string blobName)
    {
        PathValidation.ValidatePathSegment(blobName);

        return blobName;
    }

    #region Helpers

    [GeneratedRegex("[^a-z0-9-]", RegexOptions.None, 100)]
    private static partial Regex _NotAllowedCharactersRegex();

    [GeneratedRegex("^-+", RegexOptions.None, 100)]
    private static partial Regex _LeadingHyphensRegex();

    [GeneratedRegex("-+$", RegexOptions.None, 100)]
    private static partial Regex _TrailingHyphensRegex();

    #endregion
}
