// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Framework.Serializer;

namespace Framework.Blobs;

[PublicAPI]
public static class BlobStorageExtensions
{
    extension(IBlobStorage storage)
    {
        public ValueTask UploadAsync(
            string[] container,
            BlobUploadRequest request,
            CancellationToken cancellationToken = default
        )
        {
            return storage.UploadAsync(
                container,
                request.FileName,
                request.Stream,
                request.Metadata,
                cancellationToken
            );
        }

        public async Task<IReadOnlyList<BlobInfo>> GetBlobsListAsync(
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
            } while (result.HasMore && files.Count < limit.Value && await result.NextPageAsync(cancellationToken));

            return files;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask UploadContentAsync(
            string[] container,
            string blobName,
            string? contents,
            CancellationToken cancellationToken = default
        )
        {
            return storage.UploadContentAsync(container, blobName, contents, metadata: null, cancellationToken);
        }

        public async ValueTask UploadContentAsync(
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

        [RequiresUnreferencedCode("Uses JSON serialization which might require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask UploadContentAsync<T>(
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
                    options: JsonConstants.DefaultInternalJsonOptions,
                    cancellationToken: cancellationToken
                );

                memoryStream.ResetPosition();
            }

            await storage.UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken);
        }

        [RequiresUnreferencedCode("Uses JSON serialization which might require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask UploadContentAsync<T>(
            string[] container,
            string blobName,
            T? contents,
            JsonSerializerOptions options,
            CancellationToken cancellationToken = default
        )
        {
            await using var memoryStream = new MemoryStream();

            if (contents is not null)
            {
                await JsonSerializer.SerializeAsync(
                    utf8Json: memoryStream,
                    value: contents,
                    options: options,
                    cancellationToken: cancellationToken
                );

                memoryStream.ResetPosition();
            }

            await storage.UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken);
        }

        /// <summary>
        /// Uploads content serialized to JSON using source-generated metadata. AOT/trimming compatible.
        /// </summary>
        public async ValueTask UploadContentAsync<T>(
            string[] container,
            string blobName,
            T? contents,
            JsonTypeInfo<T> jsonTypeInfo,
            CancellationToken cancellationToken = default
        )
        {
            await using var memoryStream = new MemoryStream();

            if (contents is not null)
            {
                await JsonSerializer.SerializeAsync(memoryStream, contents, jsonTypeInfo, cancellationToken);
                memoryStream.ResetPosition();
            }

            await storage.UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken);
        }

        public async ValueTask<string?> GetBlobContentAsync(
            string[] container,
            string blobName,
            CancellationToken cancellationToken = default
        )
        {
            await using var result = await storage.OpenReadStreamAsync(container, blobName, cancellationToken);

            if (result is null)
            {
                return null;
            }

            return await result.Stream.GetAllTextAsync(cancellationToken);
        }

        [RequiresUnreferencedCode("Uses JSON serialization which might require types that cannot be statically analyzed.")]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask<T?> GetBlobContentAsync<T>(
            string[] container,
            string blobName,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage.GetBlobContentAsync(container, blobName, cancellationToken);

            if (content is null)
            {
                return default;
            }

            options ??= JsonConstants.DefaultInternalJsonOptions;
            var result = JsonSerializer.Deserialize<T>(content, options);

            return result;
        }

        /// <summary>
        /// Gets blob content deserialized from JSON using source-generated metadata. AOT/trimming compatible.
        /// </summary>
        public async ValueTask<T?> GetBlobContentAsync<T>(
            string[] container,
            string blobName,
            JsonTypeInfo<T> jsonTypeInfo,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage.GetBlobContentAsync(container, blobName, cancellationToken);

            if (content is null)
            {
                return default;
            }

            return JsonSerializer.Deserialize(content, jsonTypeInfo);
        }
    }
}
