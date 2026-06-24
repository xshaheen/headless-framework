// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internals;
using Headless.Core;

namespace Headless.Blobs.Aws;

/// <summary>
/// Normalizes container and blob names to comply with Amazon S3 naming rules.
/// </summary>
/// <remarks>
/// Container names (S3 buckets) are normalized to 3–63 lowercase characters containing only letters, digits,
/// dots, and hyphens, with no adjacent hyphens and dots, no IP-address pattern, and no leading or trailing
/// hyphens or dots. Blob names (object keys) pass through S3's lenient key rules without character stripping.
/// </remarks>
public sealed partial class AwsBlobNamingNormalizer : IBlobNamingNormalizer
{
    /// <summary>
    /// Normalizes a container name to a valid S3 bucket name per the
    /// <a href="https://docs.aws.amazon.com/AmazonS3/latest/dev/BucketRestrictions.html">S3 bucket naming rules</a>.
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

            // Bucket names must not be formatted as an IP address (e.g. 192.168.5.4). When the whole name is a
            // dotted-quad, drop the dots so it is no longer IP-formatted (192.168.1.1 -> 19216811).
            if (_IpAddressRegex().IsMatch(containerName))
            {
                containerName = containerName.Replace(".", string.Empty, StringComparison.Ordinal);
            }

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

    /// <summary>
    /// Validates <paramref name="blobName"/> against S3 object key rules and returns it unchanged.
    /// See the <a href="https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-keys.html">S3 object key naming guidelines</a>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="blobName"/> contains path-traversal sequences or control characters.</exception>
    public string NormalizeBlobName(string blobName)
    {
        PathValidation.ValidatePathSegment(blobName);
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

    [GeneratedRegex("^-+", RegexOptions.None, 100)]
    private static partial Regex _HyphenAtTheBeginningRegex();

    [GeneratedRegex("-+$", RegexOptions.None, 100)]
    private static partial Regex _HyphenAtTheEndRegex();

    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$", RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _IpAddressRegex();

    #endregion
}
