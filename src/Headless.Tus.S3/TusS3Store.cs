// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using Amazon.S3;
using FluentValidation;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;

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
/// </remarks>
[PublicAPI]
public sealed class TusS3Store
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

    private readonly IAmazonS3 _s3;
    private readonly TusS3StoreOptions _options;
    private readonly ILogger<TusS3Store> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Initializes a new <c>TusS3Store</c> over an existing bucket. Construction makes no network call.
    /// </summary>
    /// <param name="s3">the application's S3 client, configured for its provider</param>
    /// <param name="options">store configuration, validated here</param>
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
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<TusS3Store>();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task DeleteFileAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken) =>
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
    public Task<FileConcat?> GetUploadConcatAsync(string fileId, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<string> CreatePartialFileAsync(
        long uploadLength,
        string? metadata,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException(_NotBuiltMessage);

    /// <inheritdoc />
    public Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException(_NotBuiltMessage);
}
