// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
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
    private static readonly GuidFileIdProvider _DefaultFileIdProvider = new();

    private readonly IAmazonS3 _s3;
    private readonly TusS3StoreOptions _options;
    private readonly ITusFileIdProvider _fileIdProvider;
    private readonly ITusS3ObjectHeadersProvider _objectHeadersProvider;
    private readonly ILogger<TusS3Store> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TusS3Keys _keys;
    private readonly TusS3StateStore _stateStore;
    private readonly TusS3AppendServices _appendServices;

    /// <summary>
    /// Initializes a new <c>TusS3Store</c> over an existing bucket. Construction makes no network call.
    /// </summary>
    /// <param name="s3">the application's S3 client, configured for its provider</param>
    /// <param name="options">store configuration, validated here</param>
    /// <param name="objectHeadersProvider">
    /// optional provider of the headers each finished object carries; defaults to
    /// <see cref="DefaultTusS3ObjectHeadersProvider"/>, which stores every object as <c>application/octet-stream</c>
    /// </param>
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
        ITusS3ObjectHeadersProvider? objectHeadersProvider = null,
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
        _objectHeadersProvider = objectHeadersProvider ?? new DefaultTusS3ObjectHeadersProvider();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TusS3Store>();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keys = new TusS3Keys(options.NormalizedPrefix);
        _stateStore = new TusS3StateStore(s3, options, _keys, new TusS3RequestMemo(httpContextAccessor), _logger);
        _appendServices = new TusS3AppendServices(
            s3,
            options,
            _keys,
            _stateStore,
            // A pool of its own, sized to the largest part, so whole parts are reused across requests instead of
            // allocated on the large object heap per PATCH; the shared pool caps its arrays far below a part.
            ArrayPool<byte>.Create(options.MaxPartSize, maxArraysPerBucket: 8),
            _logger,
            _timeProvider,
            _CreateMultipartUploadAsync
        );
    }

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

    /// <summary>Loads the upload a PATCH appends to.</summary>
    /// <exception cref="TusStoreException">The upload does not exist or is gone.</exception>
    private async Task<TusS3LoadedUpload> _LoadForAppendAsync(string fileId, CancellationToken cancellationToken)
    {
        var upload = await _stateStore.LoadAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (
            upload is null
            || upload.State is { Status: TusS3UploadStatus.Gone }
            || (upload.State is { } state && _MultipartUploadLifetimePassed(state))
        )
        {
            throw new TusStoreException($"File {fileId} does not exist");
        }

        return upload;
    }

    /// <summary>Whether an append stores bytes, or only finishes or confirms a completion.</summary>
    private static bool _AcceptsBytes(TusS3LoadedUpload upload)
    {
        return upload.State is { Status: TusS3UploadStatus.Active };
    }

    /// <summary>Prepares the writer for the bytes of a PATCH to an active upload.</summary>
    private async Task<TusS3PartWriter> _BeginAppendAsync(string fileId, TusS3LoadedUpload upload)
    {
        var writer = new TusS3PartWriter(_appendServices, fileId, upload);

        try
        {
            await writer.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            await writer.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return writer;
    }

    /// <summary>Commits what the writer holds, completes the upload when that was its last byte, and reports the bytes this request appended.</summary>
    private async Task<long> _CommitAppendAsync(string fileId, TusS3PartWriter writer)
    {
        var (upload, appended) = await writer.CommitAsync().ConfigureAwait(false);

        if (upload.State is { Status: TusS3UploadStatus.Completing })
        {
            await _FinishCompletionAsync(fileId, upload).ConfigureAwait(false);
        }

        return appended;
    }

    /// <summary>
    /// Answers an append to an upload whose bytes are all stored: it finishes an interrupted completion, or confirms
    /// one this request's length declaration already finished.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="upload">the upload as loaded for the append</param>
    /// <param name="bodyBytes">the bytes of the request body, read and discarded</param>
    /// <returns>
    /// the bytes from the offset the client was told to resume at to the end, so tusdotnet answers with the full
    /// offset and raises its completion event
    /// </returns>
    /// <exception cref="TusStoreException">The upload is complete and the request still carries bytes.</exception>
    private async Task<long> _AppendToFullUploadAsync(string fileId, TusS3LoadedUpload upload, long bodyBytes)
    {
        if (upload.State is not { } state)
        {
            // Only a PATCH whose declared length completed the upload gets here with a completed upload, and its body
            // must be empty: any byte would lie past the declared length.
            return bodyBytes == 0 ? 0 : throw new TusStoreException($"File {fileId} is already complete");
        }

        // The client re-sent the PATCH whose completion failed. Its bytes are already stored, so the body is discarded
        // and only the completion runs again.
        await _FinishCompletionAsync(fileId, upload).ConfigureAwait(false);

        return state.UploadLength!.Value - (state.PreFinalOffset ?? state.Offset);
    }

    /// <summary>
    /// Starts the multipart upload an upload's parts go to, under the key its finished object will have.
    /// </summary>
    private async Task<string> _CreateMultipartUploadAsync(
        string fileId,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _options.Bucket,
            Key = _keys.FinishedObject(fileId),
        };

        // The headers belong to the multipart upload; the object completing it takes them as they are now.
        _ApplyHeaders(await _GetObjectHeadersAsync(metadata).ConfigureAwait(false), request.Headers);
        var response = await _s3.InitiateMultipartUploadAsync(request, cancellationToken).ConfigureAwait(false);

        return response.UploadId;
    }
}
