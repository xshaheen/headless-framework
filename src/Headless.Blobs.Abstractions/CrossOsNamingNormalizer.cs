// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;

namespace Headless.Blobs;

/// <summary>
/// Cross-platform naming normalizer that strips characters invalid on any OS file system.
/// Used as the default normalizer for file-system, Redis, and SFTP backends.
/// </summary>
public sealed class CrossOsNamingNormalizer : IBlobNamingNormalizer
{
    /// <inheritdoc />
    public string NormalizeContainerName(string containerName)
    {
        return _Normalize(containerName);
    }

    /// <inheritdoc />
    public string NormalizeBlobName(string blobName)
    {
        return _Normalize(blobName);
    }

    private static string _Normalize(string fileName)
    {
        // A filename cannot contain any of the following characters: \ / : * ? " < > |
        // In order to support the directory included in the blob name, remove / and \

        var sb = new StringBuilder();

        foreach (var c in fileName)
        {
            if (!FileNames.InvalidFileNameChars.Contains(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
