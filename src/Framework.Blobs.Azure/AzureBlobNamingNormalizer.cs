// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Blobs.Internals;
using Framework.Core;

namespace Framework.Blobs.Azure;

public sealed partial class AzureBlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary>
    ///https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#container-names
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
            containerName = _OnlyLettersNumbersAndDash().Replace(containerName, string.Empty);
            // Every dash (-) character must be immediately preceded and followed by a letter or number;
            // consecutive dashes are not permitted in container names.
            // Container names must start or end with a letter or number
            containerName = _ConsecutiveDashes().Replace(containerName, "-");
            containerName = _StartWithDash().Replace(containerName, string.Empty);
            containerName = _EndWithDash().Replace(containerName, string.Empty);

            // Container names must be from 3 through 63 characters long.
            if (containerName.Length < 3)
            {
                var length = containerName.Length;

                for (var i = 0; i < 3 - length; i++)
                {
                    containerName += "0";
                }
            }

            return containerName;
        }
    }

    /// <summary>
    ///https://docs.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#blob-names
    /// </summary>
    public string NormalizeBlobName(string blobName)
    {
        PathValidation.ValidatePathSegment(blobName);
        return blobName;
    }

    #region Helpers

    [GeneratedRegex("[^a-z0-9-]", RegexOptions.None, 100)]
    private static partial Regex _OnlyLettersNumbersAndDash();

    [GeneratedRegex("-{2,}", RegexOptions.None, 100)]
    private static partial Regex _ConsecutiveDashes();

    [GeneratedRegex("^-", RegexOptions.None, 100)]
    private static partial Regex _StartWithDash();

    [GeneratedRegex("-$", RegexOptions.None, 100)]
    private static partial Regex _EndWithDash();

    #endregion
}
