using System.Text;
using Framework.BuildingBlocks.Helpers;

namespace Framework.Blobs.FileSystem;

public sealed class FileSystemBlobNamingNormalizer : IBlobNamingNormalizer
{
    public string NormalizeContainerName(string containerName) => _Normalize(containerName);

    public string NormalizeBlobName(string blobName) => _Normalize(blobName);

    private static string _Normalize(string fileName)
    {
        if (!OsHelper.IsWindows)
        {
            return fileName;
        }

        // A filename cannot contain any of the following characters: \ / : * ? " < > |
        // In order to support the directory included in the blob name, remove / and \

        var sb = new StringBuilder(fileName.Length);

        foreach (var c in fileName)
        {
            if (!FileHelper.InvalidFileNameChars.Contains(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
