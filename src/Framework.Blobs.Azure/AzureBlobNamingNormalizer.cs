// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Humanizer;

namespace Framework.Blobs.Azure;

public sealed class AzureBlobNamingNormalizer : IBlobNamingNormalizer
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
            containerName = Regex.Replace(
                containerName,
                "[^a-z0-9-]",
                string.Empty,
                RegexOptions.Compiled,
                100.Milliseconds()
            );

            // Every dash (-) character must be immediately preceded and followed by a letter or number;
            // consecutive dashes are not permitted in container names.
            // Container names must start or end with a letter or number
            containerName = Regex.Replace(containerName, "-{2,}", "-", RegexOptions.Compiled, 100.Milliseconds());
            containerName = Regex.Replace(containerName, "^-", string.Empty, RegexOptions.Compiled, 100.Milliseconds());
            containerName = Regex.Replace(containerName, "-$", string.Empty, RegexOptions.Compiled, 100.Milliseconds());

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
        return blobName;
    }
}
