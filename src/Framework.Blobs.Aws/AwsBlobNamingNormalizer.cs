// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Blobs.Aws;

public sealed partial class AwsBlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary><a href="https://docs.aws.amazon.com/AmazonS3/latest/dev/BucketRestrictions.html" /></summary>
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
            // Bucket names must begin and end with a letter or number.
            // Bucket names can't start or end with hyphens adjacent to a period
            // Bucket names can't start or end with dots adjacent to a period
            // Bucket names must not be formatted as an IP address (for example, 192.168.5.4).

            containerName = _NotAllowedContainerCharactersRegex().Replace(containerName, string.Empty);
            containerName = _MultiplePeriodsRegex().Replace(containerName, ".");
            containerName = _HyphenPeriodRegex().Replace(containerName, string.Empty);
            containerName = _PeriodHyphenRegex().Replace(containerName, string.Empty);
            containerName = _HyphenAtTheBeginningRegex().Replace(containerName, string.Empty);
            containerName = _HyphenAtTheEndRegex().Replace(containerName, string.Empty);
            containerName = _PeriodAtTheBeginningRegex().Replace(containerName, string.Empty);
            containerName = _PeriodAtTheEndRegex().Replace(containerName, string.Empty);
            containerName = _IpAddressRegex().Replace(containerName, string.Empty);

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

    /// <summary><a href="https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-keys.html" /></summary>
    public string NormalizeBlobName(string blobName)
    {
        return blobName;
    }

    #region Helpers

    [GeneratedRegex("[^a-z0-9-.]", RegexOptions.None, 100)]
    private static partial Regex _NotAllowedContainerCharactersRegex();

    [GeneratedRegex(@"\.$", RegexOptions.None, 100)]
    private static partial Regex _PeriodAtTheEndRegex();

    [GeneratedRegex(@"^\.", RegexOptions.None, 100)]
    private static partial Regex _PeriodAtTheBeginningRegex();

    [GeneratedRegex(@"\.{2,}", RegexOptions.None, 100)]
    private static partial Regex _MultiplePeriodsRegex();

    [GeneratedRegex(@"-\.", RegexOptions.None, 100)]
    private static partial Regex _HyphenPeriodRegex();

    [GeneratedRegex(@"\.-", RegexOptions.None, 100)]
    private static partial Regex _PeriodHyphenRegex();

    [GeneratedRegex("^-", RegexOptions.None, 100)]
    private static partial Regex _HyphenAtTheBeginningRegex();

    [GeneratedRegex("-$", RegexOptions.None, 100)]
    private static partial Regex _HyphenAtTheEndRegex();

    [GeneratedRegex(@"^(?:^|\.)(?:2(?:5[0-5]|[0-4]\d)|1?\d?\d){4}$", RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _IpAddressRegex();

    #endregion
}
