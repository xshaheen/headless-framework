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

            if (containerName.Length > 63)
            {
                containerName = containerName[..63];
            }

            // R2 bucket names allow only lowercase letters, digits, and hyphens (no dots).
            containerName = _NotAllowedCharactersRegex().Replace(containerName, string.Empty);
            containerName = _LeadingHyphensRegex().Replace(containerName, string.Empty);
            containerName = _TrailingHyphensRegex().Replace(containerName, string.Empty);

            if (containerName.Length >= 3)
            {
                return containerName;
            }

            var length = containerName.Length;

            for (var i = 0; i < 3 - length; i++)
            {
                containerName += "0";
            }

            return containerName;
        }
    }

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
