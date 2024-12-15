// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Blobs;

[PublicAPI]
public interface IBlobNamingNormalizer
{
    string NormalizeBlobName(string blobName);

    string NormalizeContainerName(string containerName);
}
