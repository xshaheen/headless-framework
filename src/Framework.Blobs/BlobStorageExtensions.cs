// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Framework.Kernel.BuildingBlocks;

namespace Framework.Blobs;

public static class BlobStorageExtensions
{
    public static ValueTask UploadAsync(
        this IBlobStorage storage,
        string[] container,
        BlobUploadRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return storage.UploadAsync(container, request.FileName, request.Stream, request.Metadata, cancellationToken);
    }

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
        await using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(contents ?? string.Empty));

        await storage.UploadAsync(container, new BlobUploadRequest(memoryStream, blobName), cancellationToken);
    }

    public static async ValueTask UploadAsync<T>(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        T? contents,
        CancellationToken cancellationToken = default
    )
    {
        await using var memoryStream = new MemoryStream();

        await JsonSerializer.SerializeAsync(
            memoryStream,
            contents,
            FrameworkJsonConstants.DefaultInternalJsonOptions,
            cancellationToken: cancellationToken
        );

        await storage.UploadAsync(container, new BlobUploadRequest(memoryStream, blobName), cancellationToken);
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

    public static async ValueTask<T?> GetFileContentsAsync<T>(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var result = await storage.DownloadAsync(container, blobName, cancellationToken);

        if (result is null)
        {
            return default;
        }

        return await JsonSerializer.DeserializeAsync<T>(
            result.Stream,
            FrameworkJsonConstants.DefaultInternalJsonOptions,
            cancellationToken: cancellationToken
        );
    }
}
