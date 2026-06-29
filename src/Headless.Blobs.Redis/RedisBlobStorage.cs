// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Blobs.Internals;
using Headless.Checks;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace Headless.Blobs.Redis;

/// <summary>
/// <see cref="IBlobStorage"/> implementation backed by Redis hashes.
/// </summary>
/// <remarks>
/// Each container maps to two Redis hashes: a content hash (<c>{container}/</c>) holding the raw blob bytes and an
/// info hash (<c>blob-info/{container}/</c>) holding the serialized <see cref="BlobInfo"/> (created/modified/size and
/// metadata) for the same field name. Every operation routes its <see cref="BlobLocation"/> / <see cref="BlobQuery"/>
/// through <see cref="BlobLocationResolver"/> so the backend (container, key) is validated and normalized in one place.
/// All mutating operations (upload, delete, move, copy) use Lua scripts so the two hashes stay consistent, and a Polly
/// retry pipeline retries transient <see cref="RedisConnectionException"/>/timeout errors with exponential back-off and
/// jitter.
/// <para>
/// Container/bucket lifecycle is not part of this data-plane contract — it lives on the separately-registered
/// <see cref="IBlobContainerManager"/> capability (<see cref="RedisBlobContainerManager"/>). Redis has no real
/// container: the backing hash is created lazily on the first write, so an upload never has to create a container and
/// always succeeds.
/// </para>
/// <para>
/// Redis blob storage is appropriate for small or ephemeral blobs. Uploads that exceed
/// <see cref="RedisBlobStorageOptions.MaxBlobSizeBytes"/> throw <see cref="ArgumentException"/>.
/// </para>
/// </remarks>
public sealed class RedisBlobStorage : IBlobStorage
{
    private readonly ILogger _logger;
    private readonly ISerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly RedisBlobStorageOptions _options;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly IBlobNamingNormalizer _normalizer;

    // Server-page hint for the internal full-scan loops (delete-all). The public ListAsync uses BlobQuery.PageSize.
    private const int _ScanBatchSize = 1000;

    // Lua script for atomic move: rejects an occupied destination, else copies blob+info then deletes source.
    // KEYS[1] = source blob hash, KEYS[2] = source info hash
    // KEYS[3] = dest blob hash, KEYS[4] = dest info hash
    // ARGV[1] = source field name, ARGV[2] = dest field name
    // Returns 1 on success, 0 when the source does not exist, -1 when the destination is occupied (never overwrites).
    private const string _MoveScript = """
        local blobData = redis.call('HGET', KEYS[1], ARGV[1])
        local infoData = redis.call('HGET', KEYS[2], ARGV[1])
        if not blobData then return 0 end
        if redis.call('HEXISTS', KEYS[3], ARGV[2]) == 1 then return -1 end
        redis.call('HSET', KEYS[3], ARGV[2], blobData)
        redis.call('HSET', KEYS[4], ARGV[2], infoData)
        redis.call('HDEL', KEYS[1], ARGV[1])
        redis.call('HDEL', KEYS[2], ARGV[1])
        return 1
        """;

    // Lua script for atomic copy: copies blob+info without deleting source.
    // KEYS[1] = source blob hash, KEYS[2] = source info hash
    // KEYS[3] = dest blob hash, KEYS[4] = dest info hash
    // ARGV[1] = source field name, ARGV[2] = dest field name
    private const string _CopyScript = """
        local blobData = redis.call('HGET', KEYS[1], ARGV[1])
        local infoData = redis.call('HGET', KEYS[2], ARGV[1])
        if not blobData then return 0 end
        redis.call('HSET', KEYS[3], ARGV[2], blobData)
        redis.call('HSET', KEYS[4], ARGV[2], infoData)
        return 1
        """;

    // Lua script for atomic upload: writes blob data and metadata atomically.
    // KEYS[1] = blob hash, KEYS[2] = info hash
    // ARGV[1] = field name, ARGV[2] = blob data, ARGV[3] = info data
    private const string _UploadScript = """
        redis.call('HSET', KEYS[1], ARGV[1], ARGV[2])
        redis.call('HSET', KEYS[2], ARGV[1], ARGV[3])
        return 1
        """;

    // Lua script for atomic delete: deletes blob data and metadata atomically.
    // KEYS[1] = blob hash, KEYS[2] = info hash
    // ARGV[1] = field name
    // Returns 1 if either was deleted (handles orphaned data cleanup), 0 if neither existed.
    private const string _DeleteScript = """
        local blobDeleted = redis.call('HDEL', KEYS[1], ARGV[1])
        local infoDeleted = redis.call('HDEL', KEYS[2], ARGV[1])
        if blobDeleted == 1 or infoDeleted == 1 then return 1 end
        return 0
        """;

    // Lua HSCAN so the scan is routed by the hash key (cluster-safe) and we get explicit, single-page cursor control.
    // KEYS[1] = info hash; ARGV[1] = cursor, ARGV[2] = MATCH pattern, ARGV[3] = COUNT hint.
    private const string _HashScanScript =
        "return redis.call('HSCAN', KEYS[1], ARGV[1], 'MATCH', ARGV[2], 'COUNT', ARGV[3])";

    /// <summary>The Redis database obtained from the configured <see cref="IConnectionMultiplexer"/>.</summary>
    private IDatabase Database => _options.ConnectionMultiplexer.GetDatabase();

    public RedisBlobStorage(
        IOptions<RedisBlobStorageOptions> optionsAccessor,
        IJsonSerializer defaultSerializer,
        IBlobNamingNormalizer normalizer,
        TimeProvider? timeProvider = null
    )
    {
        _options = optionsAccessor.Value;
        _logger = _options.LoggerFactory?.CreateLogger(typeof(RedisBlobStorage)) ?? NullLogger.Instance;
        _serializer = _options.Serializer ?? defaultSerializer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _normalizer = normalizer;

        var pipelineLogger = _logger;

        _retryPipeline = new ResiliencePipelineBuilder { TimeProvider = _timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder()
                        .Handle<RedisConnectionException>()
                        .Handle<RedisTimeoutException>()
                        .Handle<TimeoutException>(),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        pipelineLogger.LogRedisRetry(args.AttemptNumber + 1, args.RetryDelay, args.Outcome.Exception);

                        return default;
                    },
                }
            )
            .Build();
    }

    #region Upload

    public async ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(content);

        var (blobsHash, infoHash, key) = _Resolve(location);

        try
        {
            var database = Database;

            // Validate size limit for seekable streams up-front (cheap; non-seekable is enforced while copying).
            if (_options.MaxBlobSizeBytes > 0 && content.CanSeek && content.Length > _options.MaxBlobSizeBytes)
            {
                throw new ArgumentException(
                    $"Blob exceeds maximum size of {_options.MaxBlobSizeBytes} bytes. Redis blob storage is intended for small/ephemeral blobs only.",
                    nameof(content)
                );
            }

            // Rewind seekable streams so the full payload is captured.
            if (content.CanSeek && content.Position != 0)
            {
                _logger.LogStreamPositionReset(content.Position, key);
                content.Seek(0, SeekOrigin.Begin);
            }

            await using var memory = new MemoryStream();
            await _CopyWithSizeLimitAsync(content, memory, cancellationToken).ConfigureAwait(false);
            var fileSize = memory.Length;

            // Zero-copy: TryGetBuffer avoids a ToArray() allocation.
            if (!memory.TryGetBuffer(out var blobSegment))
            {
                throw new InvalidOperationException("Failed to get buffer from MemoryStream");
            }

            var blobData = new byte[blobSegment.Count];
            Buffer.BlockCopy(blobSegment.Array!, blobSegment.Offset, blobData, 0, blobSegment.Count);

            var now = _timeProvider.GetUtcNow();

            var blobInfo = new BlobInfo
            {
                BlobKey = key,
                Created = now,
                Modified = now,
                Size = fileSize,
                Metadata = metadata,
            };

            // Serialize the small metadata payload through the buffer-first serializer's pooled-buffer path.
            var infoData = _serializer.SerializeToBytes(blobInfo)!;

            // Atomic upload via Lua so content and info never diverge.
            await _retryPipeline
                .ExecuteAsync(
                    async _ =>
                        await database
                            .ScriptEvaluateAsync(_UploadScript, [blobsHash, infoHash], [key, blobData, infoData])
                            .ConfigureAwait(false),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogErrorSavingBlob(e, key, e.Message);

            throw;
        }
    }

    #endregion

    #region Bulk Upload

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(blobs);

        if (blobs.Count == 0)
        {
            return [];
        }

        // Index results by enumeration position so results[i] describes items[i] (parallel bodies start out of order).
        var items = blobs as IReadOnlyList<BlobUploadRequest> ?? [.. blobs];
        var results = new BlobBulkResult[items.Count];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                items.Count,
                options,
                async (i, ct) =>
                {
                    var blob = items[i];

                    try
                    {
                        var location = new BlobLocation(container, blob.Path);
                        await UploadAsync(location, blob.Stream, blob.Metadata, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(true));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        results[i] = new BlobBulkResult(container, blob.Path, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (blobsHash, infoHash, key) = _Resolve(location);

        return await _DeleteResolvedAsync(blobsHash, infoHash, key, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> _DeleteResolvedAsync(
        string blobsHash,
        string infoHash,
        string key,
        CancellationToken cancellationToken
    )
    {
        _logger.LogDeletingPath(key);

        // Atomic delete via Lua so both content and info are removed together.
        var result = await _retryPipeline
            .ExecuteAsync(
                async _ =>
                    await Database
                        .ScriptEvaluateAsync(_DeleteScript, [blobsHash, infoHash], [key])
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result == 1;
    }

    #endregion

    #region Bulk Delete

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(container);
        Argument.IsNotNull(paths);

        if (paths.Count == 0)
        {
            return [];
        }

        var items = paths as IReadOnlyList<string> ?? [.. paths];
        var results = new BlobBulkResult[items.Count];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                items.Count,
                options,
                async (i, ct) =>
                {
                    var path = items[i];

                    try
                    {
                        // Build the location (validates) and resolve it through the single seam so a bulk delete can
                        // never target a raw, un-validated key. An unaddressable key fails that one item only.
                        var location = new BlobLocation(container, path);
                        var (blobsHash, infoHash, key) = _Resolve(location);
                        var deleted = await _DeleteResolvedAsync(blobsHash, infoHash, key, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(deleted));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        results[i] = new BlobBulkResult(container, path, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    public async ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (blobsHash, infoHash, prefix) = _ResolveQuery(query);
        var match = _ToRedisGlobPrefixMatch(prefix);

        // Walk the HSCAN cursor to completion to collect every field under the prefix, then delete each blob. The
        // cursor is exhausted when it returns to "0"; COUNT is only a hint, so each batch is approximate.
        var keys = new List<string>();
        var cursor = "0";

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (nextCursor, entries) = await _HashScanAsync(infoHash, match, cursor, _ScanBatchSize, cancellationToken)
                .ConfigureAwait(false);

            if (entries is not null)
            {
                for (var i = 0; i + 1 < entries.Length; i += 2)
                {
                    var field = (string?)entries[i];

                    if (field is not null)
                    {
                        keys.Add(field);
                    }
                }
            }

            cursor = nextCursor;
        } while (!string.Equals(cursor, "0", StringComparison.Ordinal));

        // HSCAN may surface the same field twice across a rehash; de-dup so the work and the returned count are clean.
        var distinctKeys = keys.Distinct(StringComparer.Ordinal).ToList();

        _logger.LogDeletingFiles(distinctKeys.Count, prefix);

        // Delete each resolved field directly (bypassing BlobLocation re-validation): the fields come from scanning
        // this container's own info hash, so they are valid backend keys even when their shape (e.g. an out-of-band
        // reserved suffix) would be rejected by new BlobLocation — re-wrapping could hard-fail and leave the container
        // un-clearable. Results are written to disjoint indices so the parallel bodies need no synchronization.
        var deleteOutcomes = new bool[distinctKeys.Count];
        var deleteErrors = new Exception?[distinctKeys.Count];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxBulkParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                distinctKeys.Count,
                options,
                async (i, ct) =>
                {
                    try
                    {
                        deleteOutcomes[i] = await _DeleteResolvedAsync(blobsHash, infoHash, distinctKeys[i], ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        deleteErrors[i] = e;
                    }
                }
            )
            .ConfigureAwait(false);

        var failures = deleteErrors.Where(static e => e is not null).Select(static e => e!).ToList();

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"DeleteAllAsync({infoHash}, {prefix}) failed for {failures.Count} blob(s).",
                failures
            );
        }

        var count = deleteOutcomes.Count(static deleted => deleted);

        _logger.LogFinishedDeletingFiles(count, prefix);

        return count;
    }

    #endregion

    #region Move / Copy

    public async ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var src = _Resolve(source);
        var dst = _Resolve(destination);

        if (
            string.Equals(src.BlobsHash, dst.BlobsHash, StringComparison.Ordinal)
            && string.Equals(src.Key, dst.Key, StringComparison.Ordinal)
        )
        {
            // A resolved self-move is a no-op: the Lua HSET-then-HDEL on the same field would delete the blob.
            return true;
        }

        _logger.LogMovingPath(src.Key, dst.Key);

        try
        {
            // Redis performs the copy-then-delete atomically inside one Lua script, which is strictly stronger than
            // the contract's "non-atomic, best-effort rollback" promise. The script rejects an occupied destination
            // (never overwrites): it returns 1 on success, 0 when the source is missing, -1 when the destination is
            // occupied — both 0 and -1 surface as false.
            var result = await _retryPipeline
                .ExecuteAsync(
                    async _ =>
                        await Database
                            .ScriptEvaluateAsync(
                                _MoveScript,
                                [src.BlobsHash, src.InfoHash, dst.BlobsHash, dst.InfoHash],
                                [src.Key, dst.Key]
                            )
                            .ConfigureAwait(false),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return (int)result == 1;
        }
        catch (Exception e)
        {
            _logger.LogErrorMovingPath(e, src.Key, dst.Key, e.Message);

            throw;
        }
    }

    public async ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        var src = _Resolve(source);
        var dst = _Resolve(destination);

        if (
            string.Equals(src.BlobsHash, dst.BlobsHash, StringComparison.Ordinal)
            && string.Equals(src.Key, dst.Key, StringComparison.Ordinal)
        )
        {
            // A resolved self-copy is a no-op; copying a blob onto itself has nothing to do.
            return true;
        }

        _logger.LogCopyingPath(src.Key, dst.Key);

        try
        {
            var result = await _retryPipeline
                .ExecuteAsync(
                    async _ =>
                        await Database
                            .ScriptEvaluateAsync(
                                _CopyScript,
                                [src.BlobsHash, src.InfoHash, dst.BlobsHash, dst.InfoHash],
                                [src.Key, dst.Key]
                            )
                            .ConfigureAwait(false),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return (int)result == 1;
        }
        catch (Exception e)
        {
            _logger.LogErrorCopyingPath(e, src.Key, dst.Key, e.Message);

            throw;
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        var (_, infoHash, key) = _Resolve(location);

        _logger.LogCheckingIfPathExists(key);

        return await _retryPipeline
            .ExecuteAsync(
                async _ => await Database.HashExistsAsync(infoHash, key).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Download / Info

    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var (blobsHash, infoHash, key) = _Resolve(location);

        _logger.LogGettingFileStream(key);

        var fileContent = await _retryPipeline
            .ExecuteAsync(
                async _ => await Database.HashGetAsync(blobsHash, key).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (fileContent.IsNull)
        {
            _logger.LogFileNotFound(key);

            return null;
        }

        // M4 fold: read the stored BlobInfo from the info hash and surface its metadata on the download result.
        var blobInfo = await _GetBlobInfoResolvedAsync(infoHash, key, cancellationToken).ConfigureAwait(false);

        var stream = new MemoryStream(fileContent!);

        return new BlobDownloadResult(stream, location.Path, blobInfo?.Metadata);
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var (_, infoHash, key) = _Resolve(location);

        _logger.LogGettingFileInfo(key);

        var blobInfo = await _GetBlobInfoResolvedAsync(infoHash, key, cancellationToken).ConfigureAwait(false);

        if (blobInfo is null)
        {
            _logger.LogFileNotFound(key);

            return null;
        }

        // Normalize BlobKey to the resolved field so identity is stable even if a stored key is stale after a move.
        return new BlobInfo
        {
            BlobKey = key,
            Created = blobInfo.Created,
            Modified = blobInfo.Modified,
            Size = blobInfo.Size,
            Metadata = blobInfo.Metadata,
        };
    }

    private async Task<BlobInfo?> _GetBlobInfoResolvedAsync(
        string infoHash,
        string key,
        CancellationToken cancellationToken
    )
    {
        var blobInfo = await _retryPipeline
            .ExecuteAsync(
                static async (state, _) =>
                    await state.Database.HashGetAsync(state.infoHash, state.key).ConfigureAwait(false),
                (Database, infoHash, key),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!blobInfo.HasValue)
        {
            return null;
        }

        return _serializer.Deserialize<BlobInfo>((byte[])blobInfo!);
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var (_, infoHash, prefix) = _ResolveQuery(query);
        var match = _ToRedisGlobPrefixMatch(prefix);
        var cursor = string.IsNullOrEmpty(query.ContinuationToken) ? "0" : query.ContinuationToken;

        // HSCAN is a cursor-based scan, NOT an ordered range query: it returns fields in an unspecified,
        // non-lexicographic order and MAY yield the same field more than once if the hash is rehashed mid-scan.
        // The opaque cursor round-trips as the page's ContinuationToken (a "0" cursor marks the end). COUNT is only a
        // hint, so the page size is approximate; we return exactly one server batch and never cap it, because capping
        // would silently drop fields the cursor has already advanced past. This is the weaker emulated tier (KTD3).
        var (nextCursor, entries) = await _HashScanAsync(infoHash, match, cursor, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = _ParseBlobInfos(entries);

        var continuationToken = string.Equals(nextCursor, "0", StringComparison.Ordinal) ? null : nextCursor;

        return new BlobPage(items, continuationToken);
    }

    private async Task<(string NextCursor, RedisResult[]? Entries)> _HashScanAsync(
        string infoHash,
        string match,
        string cursor,
        int count,
        CancellationToken cancellationToken
    )
    {
        var raw = await _retryPipeline
            .ExecuteAsync(
                async _ =>
                    await Database
                        .ScriptEvaluateAsync(_HashScanScript, [infoHash], [cursor, match, count])
                        .ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

        var top = (RedisResult[]?)raw;

        if (top is null || top.Length < 2)
        {
            return ("0", null);
        }

        // HSCAN replies are a two-element array: [next-cursor, [field1, value1, field2, value2, ...]].
        var nextCursor = (string?)top[0] ?? "0";
        var entries = (RedisResult[]?)top[1];

        return (nextCursor, entries);
    }

    private static string _ToRedisGlobPrefixMatch(string? prefix)
    {
        return string.IsNullOrEmpty(prefix) ? "*" : _EscapeRedisGlob(prefix) + "*";
    }

    private static string _EscapeRedisGlob(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (ch is '*' or '?' or '[' or ']' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private List<BlobInfo> _ParseBlobInfos(RedisResult[]? entries)
    {
        if (entries is null || entries.Length == 0)
        {
            return [];
        }

        var items = new List<BlobInfo>(entries.Length / 2);

        for (var i = 0; i + 1 < entries.Length; i += 2)
        {
            var field = (string?)entries[i];
            var value = (byte[]?)entries[i + 1];

            if (field is null || value is null)
            {
                continue;
            }

            var info = _serializer.Deserialize<BlobInfo>(value);

            if (info is null)
            {
                continue;
            }

            // The hash field name is the source of truth for the key (a stored BlobKey can be stale after a move).
            items.Add(
                new BlobInfo
                {
                    BlobKey = field,
                    Created = info.Created,
                    Modified = info.Modified,
                    Size = info.Size,
                    Metadata = info.Metadata,
                }
            );
        }

        return items;
    }

    #endregion

    #region Resolve

    private (string BlobsHash, string InfoHash, string Key) _Resolve(BlobLocation location)
    {
        var (container, key) = BlobLocationResolver.Resolve(location, _normalizer);
        var (blobsHash, infoHash) = _BuildHashKeys(container);

        return (blobsHash, infoHash, key);
    }

    private (string BlobsHash, string InfoHash, string? Prefix) _ResolveQuery(BlobQuery query)
    {
        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, _normalizer);
        var (blobsHash, infoHash) = _BuildHashKeys(container);

        return (blobsHash, infoHash, prefix);
    }

    private static (string BlobsHash, string InfoHash) _BuildHashKeys(string normalizedContainer)
    {
        var blobsHash = normalizedContainer.EnsureEndsWith('/');
        var infoHash = ("blob-info/" + normalizedContainer).EnsureEndsWith('/');

        return (blobsHash, infoHash);
    }

    #endregion

    #region Size Validation

    private async Task _CopyWithSizeLimitAsync(Stream source, MemoryStream destination, CancellationToken ct)
    {
        if (_options.MaxBlobSizeBytes <= 0)
        {
            await source.CopyToAsync(destination, 0x14000, ct).ConfigureAwait(false);

            return;
        }

        var buffer = new byte[0x14000];
        long totalBytes = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;

            if (totalBytes > _options.MaxBlobSizeBytes)
            {
                throw new ArgumentException(
                    $"Blob exceeds maximum size of {_options.MaxBlobSizeBytes} bytes. Redis blob storage is intended for small/ephemeral blobs only.",
                    nameof(source)
                );
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
        }
    }

    #endregion

    #region Dispose

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #endregion
}

internal static partial class RedisBlobStorageLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "StreamPositionReset",
        Level = LogLevel.Warning,
        Message = "Stream position was {Position}, resetting to 0 for blob {BlobKey}"
    )]
    public static partial void LogStreamPositionReset(this ILogger logger, long position, string blobKey);

    [LoggerMessage(
        EventId = 2,
        EventName = "ErrorSavingBlob",
        Level = LogLevel.Error,
        Message = "Error saving {Path}: {Message}"
    )]
    public static partial void LogErrorSavingBlob(
        this ILogger logger,
        Exception exception,
        string path,
        string message
    );

    [LoggerMessage(EventId = 3, EventName = "DeletingPath", Level = LogLevel.Trace, Message = "Deleting {Path}")]
    public static partial void LogDeletingPath(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 4,
        EventName = "DeletingFiles",
        Level = LogLevel.Information,
        Message = "Deleting {FileCount} files under prefix {Prefix}"
    )]
    public static partial void LogDeletingFiles(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 5,
        EventName = "FinishedDeletingFiles",
        Level = LogLevel.Trace,
        Message = "Finished deleting {FileCount} files under prefix {Prefix}"
    )]
    public static partial void LogFinishedDeletingFiles(this ILogger logger, int fileCount, string? prefix);

    [LoggerMessage(
        EventId = 6,
        EventName = "MovingPath",
        Level = LogLevel.Information,
        Message = "Moving {Path} to {NewPath}"
    )]
    public static partial void LogMovingPath(this ILogger logger, string path, string newPath);

    [LoggerMessage(
        EventId = 7,
        EventName = "ErrorMovingPath",
        Level = LogLevel.Error,
        Message = "Error moving {Path} to {NewPath}: {Message}"
    )]
    public static partial void LogErrorMovingPath(
        this ILogger logger,
        Exception exception,
        string path,
        string newPath,
        string message
    );

    [LoggerMessage(
        EventId = 8,
        EventName = "CopyingPath",
        Level = LogLevel.Trace,
        Message = "Copying {Path} to {TargetPath}"
    )]
    public static partial void LogCopyingPath(this ILogger logger, string path, string targetPath);

    [LoggerMessage(
        EventId = 9,
        EventName = "ErrorCopyingPath",
        Level = LogLevel.Error,
        Message = "Error copying {Path} to {TargetPath}: {Message}"
    )]
    public static partial void LogErrorCopyingPath(
        this ILogger logger,
        Exception exception,
        string path,
        string targetPath,
        string message
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "CheckingIfPathExists",
        Level = LogLevel.Trace,
        Message = "Checking if {Path} exists"
    )]
    public static partial void LogCheckingIfPathExists(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 11,
        EventName = "GettingFileStream",
        Level = LogLevel.Trace,
        Message = "Getting file stream for {Path}"
    )]
    public static partial void LogGettingFileStream(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 12,
        EventName = "FileNotFound",
        Level = LogLevel.Debug,
        Message = "File not found: {Path}"
    )]
    public static partial void LogFileNotFound(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 13,
        EventName = "GettingFileInfo",
        Level = LogLevel.Trace,
        Message = "Getting file info for {Path}"
    )]
    public static partial void LogGettingFileInfo(this ILogger logger, string path);

    [LoggerMessage(
        EventId = 14,
        EventName = "RedisRetry",
        Level = LogLevel.Warning,
        Message = "Retrying Redis operation (attempt {Attempt}) after {Delay:g}"
    )]
    public static partial void LogRedisRetry(this ILogger logger, int attempt, TimeSpan delay, Exception? exception);
}
