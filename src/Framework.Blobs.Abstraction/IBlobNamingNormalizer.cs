// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using Framework.IO;

namespace Framework.Blobs;

[PublicAPI]
public interface IBlobNamingNormalizer
{
    string NormalizeBlobName(string blobName);

    string NormalizeContainerName(string containerName);
}

public sealed class CrossOsNamingNormalizer : IBlobNamingNormalizer
{
    public string NormalizeContainerName(string containerName) => _Normalize(containerName);

    public string NormalizeBlobName(string blobName) => _Normalize(blobName);

    private static string _Normalize(string fileName)
    {
        // A filename cannot contain any of the following characters: \ / : * ? " < > |
        // In order to support the directory included in the blob name, remove / and \

        var sb = ZString.CreateStringBuilder();

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
