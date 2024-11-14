// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

namespace Framework.Blobs;

public static class BlobStorageExtensions
{
    public static async Task<IReadOnlyList<BlobInfo>> GetFileListAsync(
        this IBlobStorage storage,
        string[] container,
        string? blobSearchPattern = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<BlobInfo>();

        limit ??= int.MaxValue;

        var result = await storage.GetPagedListAsync(container, blobSearchPattern, limit.Value, cancellationToken);

        do
        {
            files.AddRange(result.Blobs);
        } while (result.HasMore && files.Count < limit.Value && await result.NextPageAsync());

        return files;
    }

    public static async ValueTask UploadAsync(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        string? contents,
        CancellationToken cancellationToken = default
    )
    {
        await storage.UploadAsync(
            container,
            new BlobUploadRequest(new MemoryStream(Encoding.UTF8.GetBytes(contents ?? string.Empty)), blobName),
            cancellationToken
        );
    }

    public static async ValueTask<string?> GetFileContentsAsync(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var result = await storage.DownloadAsync(container, blobName, cancellationToken);

        if (result is null)
        {
            return null;
        }

        using var reader = new StreamReader(result.Stream);

        return await reader.ReadToEndAsync(cancellationToken);
    }
}
