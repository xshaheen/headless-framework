// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Framework.Constants;

namespace Framework.Blobs;

[PublicAPI]
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

    public static async Task<IReadOnlyList<BlobInfo>> GetBlobsListAsync(
        this IBlobStorage storage,
        string[] container,
        string? blobSearchPattern = null,
        int? limit = null,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<BlobInfo>();

        limit ??= 1_000_000;

        var result = await storage.GetPagedListAsync(container, blobSearchPattern, limit.Value, cancellationToken);

        do
        {
            files.AddRange(result.Blobs);
        } while (result.HasMore && files.Count < limit.Value && await result.NextPageAsync());

        return files;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UploadContentAsync(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        string? contents,
        CancellationToken cancellationToken = default
    )
    {
        return storage.UploadContentAsync(container, blobName, contents, metadata: null, cancellationToken);
    }

    public static async ValueTask UploadContentAsync(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        string? contents,
        Dictionary<string, string?>? metadata,
        CancellationToken cancellationToken = default
    )
    {
        await using var memoryStream = new MemoryStream();
        await memoryStream.WriteTextAsync(contents, cancellationToken);
        memoryStream.ResetPosition();

        await storage.UploadAsync(container, blobName, memoryStream, metadata, cancellationToken);
    }

    public static async ValueTask UploadContentAsync<T>(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        T? contents,
        CancellationToken cancellationToken = default
    )
    {
        await using var memoryStream = new MemoryStream();

        if (contents is not null)
        {
            await JsonSerializer.SerializeAsync(
                utf8Json: memoryStream,
                value: contents,
                options: FrameworkJsonConstants.DefaultInternalJsonOptions,
                cancellationToken: cancellationToken
            );

            memoryStream.ResetPosition();
        }

        await storage.UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken);
    }

    public static async ValueTask<string?> GetBlobContentAsync(
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

        var contents = await result.Stream.GetAllTextAsync(cancellationToken);

        return contents;
    }

    public static async ValueTask<T?> GetBlobContentAsync<T>(
        this IBlobStorage storage,
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var content = await GetBlobContentAsync(storage, container, blobName, cancellationToken);

        if (content is null)
        {
            return default;
        }

        var result = JsonSerializer.Deserialize<T>(content, FrameworkJsonConstants.DefaultInternalJsonOptions);

        return result;
    }
}
