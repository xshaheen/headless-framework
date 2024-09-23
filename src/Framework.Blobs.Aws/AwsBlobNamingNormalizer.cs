// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.RegularExpressions;
using Framework.Kernel.BuildingBlocks.Helpers.System;
using Humanizer;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary>https://docs.aws.amazon.com/AmazonS3/latest/dev/BucketRestrictions.html</summary>
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

            // Bucket names can consist only of lowercase letters, numbers, dots (.), and hyphens (-).
            containerName = Regex.Replace(
                containerName,
                "[^a-z0-9-.]",
                string.Empty,
                RegexOptions.Compiled,
                100.Seconds()
            );

            // Bucket names must begin and end with a letter or number.
            // Bucket names must not be formatted as an IP address (for example, 192.168.5.4).
            // Bucket names can't start or end with hyphens adjacent to period
            // Bucket names can't start or end with dots adjacent to period
            containerName = Regex.Replace(containerName, "\\.{2,}", ".", RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "-\\.", string.Empty, RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "\\.-", string.Empty, RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "^-", string.Empty, RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "-$", string.Empty, RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "^\\.", string.Empty, RegexOptions.Compiled, 100.Seconds());
            containerName = Regex.Replace(containerName, "\\.$", string.Empty, RegexOptions.Compiled, 100.Seconds());

            containerName = Regex.Replace(
                containerName,
                @"^(?:^|\.)(?:2(?:5[0-5]|[0-4]\d)|1?\d?\d){4}$",
                string.Empty,
                RegexOptions.Compiled,
                100.Seconds()
            );

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

    /// <summary>https://docs.aws.amazon.com/AmazonS3/latest/dev/UsingMetadata.html</summary>
    public string NormalizeBlobName(string blobName)
    {
        return blobName;
    }
}
