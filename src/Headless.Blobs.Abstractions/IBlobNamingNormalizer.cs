// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;

namespace Headless.Blobs;

/// <summary>
/// Sanitizes blob and container names so they are safe and valid for a specific storage provider.
/// </summary>
/// <remarks>
/// Each provider package registers its own implementation. Normalizers are applied automatically by the storage
/// implementation — callers do not need to pre-sanitize names before passing them to <see cref="IBlobStorage"/>.
/// </remarks>
[PublicAPI]
public interface IBlobNamingNormalizer
{
    /// <summary>Normalizes a blob name to be valid for the target provider.</summary>
    /// <param name="blobName">The raw blob name to normalize.</param>
    /// <returns>A provider-safe blob name. The transformation is provider-specific and lossy — distinct inputs may produce the same output.</returns>
    string NormalizeBlobName(string blobName);

    /// <summary>Normalizes a container name to be valid for the target provider.</summary>
    /// <param name="containerName">The raw container name to normalize.</param>
    /// <returns>A provider-safe container name. The transformation is provider-specific and lossy.</returns>
    string NormalizeContainerName(string containerName);
}

/// <summary>
/// Cross-platform naming normalizer that strips characters invalid on any OS file system.
/// Used as the default normalizer for file-system, Redis, and SFTP backends.
/// </summary>
public sealed class CrossOsNamingNormalizer : IBlobNamingNormalizer
{
    /// <inheritdoc />
    public string NormalizeContainerName(string containerName) => _Normalize(containerName);

    /// <inheritdoc />
    public string NormalizeBlobName(string blobName) => _Normalize(blobName);

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
