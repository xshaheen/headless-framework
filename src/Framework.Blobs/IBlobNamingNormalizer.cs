namespace Framework.Blobs;

public interface IBlobNamingNormalizer
{
    string NormalizeBlobName(string blobName);

    string NormalizeContainerName(string containerName);
}
