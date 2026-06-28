// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Headless.Blobs.Internals;
using Headless.Serializer;

namespace Headless.Blobs;

/// <summary>
/// Extension methods on <see cref="IBlobStorage"/> for common listing, upload, and download convenience patterns.
/// </summary>
[PublicAPI]
public static class BlobStorageExtensions
{
    extension(IBlobStorage storage)
    {
        /// <summary>
        /// Streams every blob matched by <paramref name="query"/> as an asynchronous sequence, transparently fetching
        /// each page from <see cref="IBlobStorage.ListAsync"/> and following the opaque continuation token until it is
        /// <see langword="null"/>.
        /// </summary>
        /// <param name="query">The container plus optional prefix and page size to enumerate.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async sequence of <see cref="BlobInfo"/> records spanning all pages.</returns>
        public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
            BlobQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var current = query;

            while (true)
            {
                var page = await storage.ListAsync(current, cancellationToken).ConfigureAwait(false);

                foreach (var blob in page.Items)
                {
                    yield return blob;
                }

                if (page.ContinuationToken is null)
                {
                    yield break;
                }

                current = new BlobQuery(current.Container, current.Prefix, current.PageSize, page.ContinuationToken);
            }
        }

        /// <summary>
        /// Streams the blobs matched by <paramref name="query"/> whose keys also match the client-side glob
        /// <paramref name="globPattern"/> (<c>*</c> and <c>?</c> wildcards) via the shared matcher.
        /// </summary>
        /// <param name="query">The container plus optional prefix and page size to enumerate.</param>
        /// <param name="globPattern">A glob pattern matched against each blob's <see cref="BlobInfo.BlobKey"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async sequence of matching <see cref="BlobInfo"/> records.</returns>
        /// <remarks>
        /// When <see cref="BlobQuery.Prefix"/> and the glob's literal prefix are mutually exclusive (neither is a
        /// prefix of the other), no key can satisfy both, so this yields an empty sequence. That is an empty match,
        /// not an error — compare the two prefixes yourself first if you need to distinguish the cases.
        /// </remarks>
        public async IAsyncEnumerable<BlobInfo> GetBlobsAsync(
            BlobQuery query,
            string globPattern,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            var matcher = BlobStorageHelpers.CreateGlobMatcher(globPattern);
            var narrowedQuery = _TryNarrowGlobQuery(query, globPattern);

            if (narrowedQuery is null)
            {
                yield break;
            }

            await foreach (var blob in storage.GetBlobsAsync(narrowedQuery, cancellationToken).ConfigureAwait(false))
            {
                if (matcher(blob.BlobKey))
                {
                    yield return blob;
                }
            }
        }

        /// <summary>
        /// Materializes the blobs matched by <paramref name="query"/> into a list, streaming pages until exhausted or
        /// <paramref name="limit"/> is reached.
        /// </summary>
        /// <param name="query">The container plus optional prefix and page size to enumerate.</param>
        /// <param name="limit">Maximum total number of blobs to return. Defaults to 1 000 000 when not specified.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A materialized list of matching <see cref="BlobInfo"/> records.</returns>
        public async Task<IReadOnlyList<BlobInfo>> GetBlobsListAsync(
            BlobQuery query,
            int? limit = null,
            CancellationToken cancellationToken = default
        )
        {
            var max = limit ?? 1_000_000;
            var files = new List<BlobInfo>();

            await foreach (var blob in storage.GetBlobsAsync(query, cancellationToken).ConfigureAwait(false))
            {
                files.Add(blob);

                if (files.Count >= max)
                {
                    break;
                }
            }

            return files;
        }

        /// <summary>
        /// Uploads a UTF-8 string as the blob's content with no metadata.
        /// </summary>
        /// <param name="location">The blob to write.</param>
        /// <param name="contents">Text content to upload. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask UploadContentAsync(
            BlobLocation location,
            string? contents,
            CancellationToken cancellationToken = default
        )
        {
            return storage.UploadContentAsync(location, contents, metadata: null, cancellationToken);
        }

        /// <summary>
        /// Uploads a UTF-8 string as the blob's content, optionally storing metadata alongside it.
        /// </summary>
        /// <param name="location">The blob to write.</param>
        /// <param name="contents">Text content to upload. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="metadata">Optional metadata key/value pairs (non-null values).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask UploadContentAsync(
            BlobLocation location,
            string? contents,
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken = default
        )
        {
            await using var memoryStream = new MemoryStream();
            await memoryStream.WriteTextAsync(contents, cancellationToken).ConfigureAwait(false);
            memoryStream.ResetPosition();

            await storage.UploadAsync(location, memoryStream, metadata, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using reflection-based serialization and uploads the result.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="location">The blob to write.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask UploadContentAsync<T>(
            BlobLocation location,
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

            await storage.UploadAsync(location, memoryStream, metadata: null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using the supplied <see cref="JsonSerializerOptions"/> and
        /// uploads the result.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="location">The blob to write.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="options">Serializer options to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask UploadContentAsync<T>(
            BlobLocation location,
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

            await storage.UploadAsync(location, memoryStream, metadata: null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes <paramref name="contents"/> to JSON using source-generated type metadata and uploads the result.
        /// AOT and trimming compatible.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="location">The blob to write.</param>
        /// <param name="contents">The object to serialize. A <see langword="null"/> value uploads an empty blob.</param>
        /// <param name="jsonTypeInfo">Source-generated type metadata for <typeparamref name="T"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask UploadContentAsync<T>(
            BlobLocation location,
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

            await storage.UploadAsync(location, memoryStream, metadata: null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads the blob's content as a UTF-8 string, or returns <see langword="null"/> if the blob does not exist.
        /// </summary>
        /// <param name="location">The blob to read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The decoded text content, or <see langword="null"/> when the blob is not found.</returns>
        public async ValueTask<string?> GetBlobContentAsync(
            BlobLocation location,
            CancellationToken cancellationToken = default
        )
        {
            await using var result = await storage
                .OpenReadStreamAsync(location, cancellationToken)
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
        /// <param name="location">The blob to read.</param>
        /// <param name="options">Optional serializer options. Defaults to the framework's internal options when <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the blob is not found.</returns>
        /// <remarks>Not AOT/trim compatible. In AOT scenarios prefer the overload that accepts a source-generated <see cref="JsonTypeInfo{T}"/>.</remarks>
        [RequiresUnreferencedCode(
            "Uses JSON serialization which might require types that cannot be statically analyzed."
        )]
        [RequiresDynamicCode("Uses JSON serialization which might require dynamic code generation.")]
        public async ValueTask<T?> GetBlobContentAsync<T>(
            BlobLocation location,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage.GetBlobContentAsync(location, cancellationToken).ConfigureAwait(false);

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
        /// <param name="location">The blob to read.</param>
        /// <param name="jsonTypeInfo">Source-generated type metadata for <typeparamref name="T"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the blob is not found.</returns>
        public async ValueTask<T?> GetBlobContentAsync<T>(
            BlobLocation location,
            JsonTypeInfo<T> jsonTypeInfo,
            CancellationToken cancellationToken = default
        )
        {
            var content = await storage.GetBlobContentAsync(location, cancellationToken).ConfigureAwait(false);

            if (content is null)
            {
                return default;
            }

            return JsonSerializer.Deserialize(content, jsonTypeInfo);
        }
    }

    private static BlobQuery? _TryNarrowGlobQuery(BlobQuery query, string globPattern)
    {
        if (query.ContinuationToken is not null)
        {
            return query;
        }

        var literalPrefix = BlobStorageHelpers.GetLiteralPrefix(globPattern);

        if (string.IsNullOrEmpty(literalPrefix))
        {
            return query;
        }

        if (string.IsNullOrEmpty(query.Prefix))
        {
            return _TryCreateQueryWithPrefix(query, literalPrefix);
        }

        if (literalPrefix.StartsWith(query.Prefix, StringComparison.Ordinal))
        {
            return _TryCreateQueryWithPrefix(query, literalPrefix);
        }

        return query.Prefix.StartsWith(literalPrefix, StringComparison.Ordinal) ? query : null;
    }

    private static BlobQuery _TryCreateQueryWithPrefix(BlobQuery query, string prefix)
    {
        try
        {
            return new BlobQuery(query.Container, prefix, query.PageSize, query.ContinuationToken);
        }
        catch (ArgumentException)
        {
            // A glob can contain a literal prefix that is not a valid BlobQuery prefix. Keep the old behavior in that
            // case: enumerate the caller's query and let the client-side matcher decide.
            return query;
        }
    }
}
