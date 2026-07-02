// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internals;
using Headless.Core;

namespace Headless.Blobs.Azure;

/// <summary>
/// Normalizes container and blob names to comply with Azure Blob Storage naming rules.
/// </summary>
/// <remarks>
/// Container names are normalized to 3–63 lowercase characters containing only letters, digits, and hyphens,
/// with no consecutive hyphens and no leading or trailing hyphens. Blob names (blob paths) pass through
/// Azure's lenient key rules without character stripping.
/// </remarks>
public sealed partial class AzureBlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary>
    /// Normalizes a container name to comply with
    /// <a href="https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#container-names">Azure container naming rules</a>.
    /// </summary>
    public string NormalizeContainerName(string containerName)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            // All letters in a container name must be lowercase.
            containerName = containerName.ToLowerInvariant();

            // Container names must be from 3 through 63 characters long.
            if (containerName.Length > 63)
            {
                containerName = containerName[..63];
            }

            // Container names can contain only letters, numbers, and the dash (-) character.
            containerName = OnlyLettersNumbersAndDash.Replace(containerName, string.Empty);
            // Every dash (-) character must be immediately preceded and followed by a letter or number;
            // consecutive dashes are not permitted in container names.
            // Container names must start or end with a letter or number
            containerName = ConsecutiveDashes.Replace(containerName, "-");
            containerName = StartWithDash.Replace(containerName, string.Empty);
            containerName = EndWithDash.Replace(containerName, string.Empty);

            // Container names must be from 3 through 63 characters long.
            return containerName.Length >= 3 ? containerName : containerName.PadRight(3, '0');
        }
    }

    /// <summary>
    /// Validates <paramref name="blobName"/> against Azure blob naming rules and returns it unchanged.
    /// See <a href="https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#blob-names">Azure blob naming rules</a>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> contains path-traversal sequences or control characters.</exception>
    public string NormalizeBlobName(string blobName)
    {
        PathValidation.ValidatePathSegment(blobName);
        return blobName;
    }

    #region Helpers

    [GeneratedRegex("[^a-z0-9-]", RegexOptions.None, 100)]
    private static partial Regex OnlyLettersNumbersAndDash { get; }

    [GeneratedRegex("-{2,}", RegexOptions.None, 100)]
    private static partial Regex ConsecutiveDashes { get; }

    [GeneratedRegex("^-", RegexOptions.None, 100)]
    private static partial Regex StartWithDash { get; }

    [GeneratedRegex("-$", RegexOptions.None, 100)]
    private static partial Regex EndWithDash { get; }

    #endregion
}
