// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Headless.Serializer;

namespace Headless.Blobs;

/// <summary>
/// Extension methods on <see cref="IBlobStorage"/> for common upload, download, and listing convenience patterns.
/// </summary>
[PublicAPI]
public static class BlobStorageExtensions
{
    extension(IBlobStorage storage)
    {
        /// <summary>
        /// Uploads a blob described by a <see cref="BlobUploadRequest"/>, delegating to
        /// <see cref="IBlobStorage.UploadAsync"/>.
        /// </summary>
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

        /// <summary>
        /// Collects all blobs in <paramref name="container"/> that match <paramref name="blobSearchPattern"/> into a
        /// list, fetching pages via <see cref="IBlobStorage.GetPagedListAsync"/> until all results are gathered or
        /// <paramref name="limit"/> is reached.
        /// </summary>
        /// <param name="container">Hierarchical path segments identifying the container.</param>
        /// <param name="blobSearchPattern">Optional glob pattern to filter blob names.</param>
        /// <param name="limit">Maximum total number of blobs to return. Defaults to 1 000 000 when not specified.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A materialized list of matching <see cref="BlobInfo"/> records.</returns>
        public async Task<IReadOnlyList<BlobInfo>> GetBlobsListAsync(
            string[] container,
            string? blobSearchPattern = null,
            int? limit = null,
            CancellationToken cancellationToken = default
        )
        {
            var files = new List<BlobInfo>();

            limit ??= 1_000_000;

            var result = await storage
                .GetPagedListAsync(container, blobSearchPattern, limit.Value, cancellationToken)
                .ConfigureAwait(false);

            do
            {
                files.AddRange(result.Blobs);
            } while (
                result.HasMore
                && files.Count < limit.Value
                && await result.NextPageAsync(cancellationToken).ConfigureAwait(false)
            );

            return files;
        }

        /// <summary>
        /// Uploads a UTF-8 string as the blob's content with no metadata.
        /// </summary>
        /// <param name="container">Hierarchical path segments identifying the target container.</param>
        /// <param name="blobName">The blob name within the container.</param>
        /// <param name="contents">Text content to upload. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
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

        /// <summary>
        /// Uploads a UTF-8 string as the blob's content, optionally storing metadata alongside it.
        /// </summary>
        /// <param name="container">Hierarchical path segments identifying the target container.</param>
        /// <param name="blobName">The blob name within the container.</param>
        /// <param name="contents">Text content to upload. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="metadata">Optional metadata key/value pairs. Providers without metadata support silently ignore this.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask UploadContentAsync(
            string[] container,
            string blobName,
            string? contents,
            Dictionary<string, string?>? metadata,
            CancellationToken cancellationToken = default
        )
        {
            await using var memoryStream = new MemoryStream();
            await memoryStream.WriteTextAsync(contents, cancellationToken).ConfigureAwait(false);
            memoryStream.ResetPosition();

            await storage
                .UploadAsync(container, blobName, memoryStream, metadata, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using reflection-based serialization and uploads the result.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="container">Hierarchical path segments identifying the target container.</param>
        /// <param name="blobName">The blob name within the container.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
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
                await JsonSerializer
                    .SerializeAsync(
                        utf8Json: memoryStream,
                        value: contents,
                        options: JsonConstants.DefaultInternalJsonOptions,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);

                memoryStream.ResetPosition();
            }

            await storage
                .UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using the supplied <see cref="JsonSerializerOptions"/> and
        /// uploads the result.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="container">Hierarchical path segments identifying the target container.</param>
        /// <param name="blobName">The blob name within the container.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="options">Serializer options to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
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
                await JsonSerializer
                    .SerializeAsync(
                        utf8Json: memoryStream,
                        value: contents,
                        options: options,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);

                memoryStream.ResetPosition();
            }

            await storage
                .UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using source-generated type metadata and uploads the result.
        /// AOT and trimming compatible.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="container">Hierarchical path segments identifying the target container.</param>
        /// <param name="blobName">The blob name within the container.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="jsonTypeInfo">Source-generated type metadata for <typeparamref name="T"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
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
                await JsonSerializer
                    .SerializeAsync(memoryStream, contents, jsonTypeInfo, cancellationToken)
                    .ConfigureAwait(false);
                memoryStream.ResetPosition();
            }

            await storage
                .UploadAsync(container, blobName, memoryStream, metadata: null, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the blob's content as a UTF-8 string, or returns <see langword="null"/> if the blob does not exist.
        /// </summary>
        /// <param name="container">Hierarchical path segments identifying the container.</param>
        /// <param name="blobName">The blob name to read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The decoded text content, or <see langword="null"/> when the blob is not found.</returns>
        public async ValueTask<string?> GetBlobContentAsync(
            string[] container,
            string blobName,
            CancellationToken cancellationToken = default
        )
        {
            await using var result = await storage
                .OpenReadStreamAsync(container, blobName, cancellationToken)
                .ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            return await result.Stream.GetAllTextAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the blob and deserializes its JSON content to <typeparamref name="T"/> using
        /// reflection-based serialization.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="container">Hierarchical path segments identifying the container.</param>
        /// <param name="blobName">The blob name to read.</param>
        /// <param name="options">Optional serializer options. Defaults to the framework's internal options when <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the blob is not found.</returns>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask<T?> GetBlobContentAsync<T>(
            string[] container,
            string blobName,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage
                .GetBlobContentAsync(container, blobName, cancellationToken)
                .ConfigureAwait(false);

            if (content is null)
            {
                return default;
            }

            options ??= JsonConstants.DefaultInternalJsonOptions;
            var result = JsonSerializer.Deserialize<T>(content, options);

            return result;
        }

        /// <summary>
        /// Downloads the blob and deserializes its JSON content to <typeparamref name="T"/> using source-generated
        /// type metadata. AOT and trimming compatible.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="container">Hierarchical path segments identifying the container.</param>
        /// <param name="blobName">The blob name to read.</param>
        /// <param name="jsonTypeInfo">Source-generated type metadata for <typeparamref name="T"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the blob is not found.</returns>
        public async ValueTask<T?> GetBlobContentAsync<T>(
            string[] container,
            string blobName,
            JsonTypeInfo<T> jsonTypeInfo,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage
                .GetBlobContentAsync(container, blobName, cancellationToken)
                .ConfigureAwait(false);

            if (content is null)
            {
                return default;
            }

            return JsonSerializer.Deserialize(content, jsonTypeInfo);
        }
    }
}
