// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Blobs;

public interface IBlobNamingNormalizer
{
    string NormalizeBlobName(string blobName);

    string NormalizeContainerName(string containerName);
}
