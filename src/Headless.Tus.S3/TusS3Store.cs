// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using Amazon.S3;
using Amazon.S3.Model;
using FluentValidation;
using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Stores.FileIdProviders;

namespace Headless.Tus;

/// <summary>
/// TUS resumable-upload store backed by Amazon S3, Cloudflare R2, or an S3-compatible server.
/// </summary>
/// <remarks>
/// <para>
/// The store takes the application's <see cref="IAmazonS3"/> client and never builds one: the endpoint, credentials,
/// path style, authentication region, and checksum mode are the application's to configure for its provider.
/// <see cref="TusS3StoreOptions.Provider"/> only selects the per-request settings the store sends.
/// </para>
/// <para>
/// The store never creates its bucket. <c>AddTusS3Store</c> registers it as a provisioned resource, so a deploy step
/// creates it with <c>resources provision -k S3Bucket</c> and a starting host only checks that it exists; a manually
/// constructed store needs the bucket created beforehand.
/// </para>
/// <para>
/// <b>Write fencing:</b> the per-file lock is best-effort, so a request can lose it without noticing (a process pause,
/// a lock-backend failover) while a second request for the same upload proceeds. Each upload's state object is
/// therefore the fence: every state write sends the ETag the request read as <c>If-Match</c>. A writer whose view is
/// stale fails with the provider's <see cref="AmazonS3Exception"/> (412, 409, or 404), which is deliberately
/// <em>not</em> translated into a <c>TusStoreException</c>: tusdotnet answers that with 400, which tus clients treat
/// as fatal, while the untranslated exception surfaces as a 500 that clients retry by asking HEAD for the committed
/// offset.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class TusS3Store
    : ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    private const string _NotBuiltMessage = "The S3 store does not implement this tus operation yet.";

    private static readonly GuidFileIdProvider _DefaultFileIdProvider = new();

    private readonly IAmazonS3 _s3;
    private readonly TusS3StoreOptions _options;
    private readonly ITusFileIdProvider _fileIdProvider;
    private readonly ILogger<TusS3Store> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TusS3Keys _keys;
    private readonly TusS3StateStore _stateStore;

    /// <summary>
    /// Initializes a new <c>TusS3Store</c> over an existing bucket. Construction makes no network call.
    /// </summary>
    /// <param name="s3">the application's S3 client, configured for its provider</param>
    /// <param name="options">store configuration, validated here</param>
    /// <param name="fileIdProvider">
    /// optional strategy for generating TUS file identifiers; defaults to a GUID-based provider
    /// </param>
    /// <param name="loggerFactory">optional logger factory; defaults to the null logger</param>
    /// <param name="timeProvider">optional time abstraction; defaults to <c>TimeProvider.System</c></param>
    /// <param name="httpContextAccessor">
    /// optional accessor that lets the store read an upload's state once per tus request; without it the store reads
    /// the state on every call and stays correct
    /// </param>
    /// <exception cref="ValidationException">The options are invalid.</exception>
    public TusS3Store(
        IAmazonS3 s3,
        TusS3StoreOptions options,
        ITusFileIdProvider? fileIdProvider = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null,
        IHttpContextAccessor? httpContextAccessor = null
    )
    {
        Argument.IsNotNull(s3);
        Argument.IsNotNull(options);
        // The store is often constructed inside a tusdotnet configuration factory, outside the options pipeline, so
        // validation runs here to fail fast instead of surfacing as provider errors on the first upload.
        new TusS3StoreOptionsValidator().ValidateAndThrow(options);

        _s3 = s3;
        _options = options;
        _fileIdProvider = fileIdProvider ?? _DefaultFileIdProvider;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TusS3Store>();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keys = new TusS3Keys(options.NormalizedPrefix);
        _stateStore = new TusS3StateStore(s3, options, _keys, new TusS3RequestMemo(httpContextAccessor), _logger);
    }

    /// <inheritdoc />
    public Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task DeleteFileAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException(_NotBuiltMessage);

    /// <summary>
    /// Validates an externally supplied file id against the configured <c>ITusFileIdProvider</c>, then against the
    /// key rules, so an id the provider would never issue is refused before it reaches storage.
    /// </summary>
    /// <exception cref="TusStoreException">The id is invalid (tusdotnet answers 400).</exception>
    private async Task _EnsureValidFileIdAsync(string fileId)
    {
        if (!await _fileIdProvider.ValidateId(fileId).ConfigureAwait(false))
        {
            throw new TusStoreException($"Invalid TUS file id: '{fileId}'.");
        }

        TusS3Keys.EnsureValid(fileId);
    }

    /// <summary>
    /// Writes the finished object of an upload that completed with no bytes. Every finished object the store writes
    /// outside a multipart upload goes through here, so the headers the object carries are decided in one place.
    /// </summary>
    /// <exception cref="AmazonS3Exception">A finished object already exists under the id (412).</exception>
    private async Task _PutEmptyFinishedObjectAsync(string fileId, CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = _keys.FinishedObject(fileId),
            InputStream = Stream.Null,
            AutoCloseStream = false,
            IfNoneMatch = "*",
        };

        TusS3RequestPresets.Apply(request, _options.Provider);

        await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
