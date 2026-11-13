// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;

namespace Headless.Tus.Internal;

/// <summary>
/// The store tusdotnet works against for one request to an upload endpoint. It wraps the host's store, writes the
/// caller's stamp on every upload it creates, and leaves the stamp out of every metadata read, so the client never
/// sees it. Whether the caller may reach an upload is decided before tusdotnet calls the store, not here.
/// </summary>
/// <remarks>
/// tusdotnet finds out what a store can do from the interfaces it implements, and this type implements all of
/// them. The endpoint therefore tells tusdotnet which extensions the wrapped store really has, and a member whose
/// capability the wrapped store lacks is never called. Reading the request body is the exception: it always goes
/// through the pipeline member, because the endpoint inspects the body through its pipe, and a store that reads
/// only from a stream gets the pipe as one.
/// </remarks>
/// <param name="inner">The host's store.</param>
/// <param name="caller">
/// The stamp an upload created by this request gets, or <see langword="null"/> when the request has no owner.
/// </param>
internal sealed class TusStampedStore(ITusStore inner, TusUploadStamp? caller)
    : ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusChecksumStore,
        ITusConcatenationStore,
        ITusReadableStore
{
    public Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.FileExistAsync(fileId, cancellationToken);
    }

    public Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.GetUploadLengthAsync(fileId, cancellationToken);
    }

    public Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.GetUploadOffsetAsync(fileId, cancellationToken);
    }

    public Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        return inner.AppendDataAsync(fileId, stream, cancellationToken);
    }

    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        if (inner is ITusPipelineStore pipelineStore)
        {
            return await pipelineStore.AppendDataAsync(fileId, pipeReader, cancellationToken).ConfigureAwait(false);
        }

        // The pipe stays open: tusdotnet owns the request's reader and completes it itself.
        var stream = pipeReader.AsStream(leaveOpen: true);

        await using (stream.ConfigureAwait(false))
        {
            return await inner.AppendDataAsync(fileId, stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The upload this request created, or <see langword="null"/> when it created none. A write to that upload is
    /// the data its creating request carries, not a request of its own.
    /// </summary>
    public string? CreatedFileId { get; private set; }

    public async Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        CreatedFileId = await _Inner<ITusCreationStore>()
            .CreateFileAsync(uploadLength, _Stamp(metadata), cancellationToken)
            .ConfigureAwait(false);

        return CreatedFileId;
    }

    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var metadata = await _Inner<ITusCreationStore>()
            .GetUploadMetadataAsync(fileId, cancellationToken)
            .ConfigureAwait(false);

        return TusUploadStamp.Strip(metadata);
    }

    public Task SetUploadLengthAsync(string fileId, long uploadLength, CancellationToken cancellationToken)
    {
        return _Inner<ITusCreationDeferLengthStore>().SetUploadLengthAsync(fileId, uploadLength, cancellationToken);
    }

    public Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        return _Inner<ITusTerminationStore>().DeleteFileAsync(fileId, cancellationToken);
    }

    public Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken)
    {
        return _Inner<ITusExpirationStore>().SetExpirationAsync(fileId, expires, cancellationToken);
    }

    public Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken)
    {
        return _Inner<ITusExpirationStore>().GetExpirationAsync(fileId, cancellationToken);
    }

    public Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        return _Inner<ITusExpirationStore>().GetExpiredFilesAsync(cancellationToken);
    }

    public Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        return _Inner<ITusExpirationStore>().RemoveExpiredFilesAsync(cancellationToken);
    }

    public Task<IEnumerable<string>> GetSupportedAlgorithmsAsync(CancellationToken cancellationToken)
    {
        return _Inner<ITusChecksumStore>().GetSupportedAlgorithmsAsync(cancellationToken);
    }

    public Task<bool> VerifyChecksumAsync(
        string fileId,
        string algorithm,
        byte[] checksum,
        CancellationToken cancellationToken
    )
    {
        return _Inner<ITusChecksumStore>().VerifyChecksumAsync(fileId, algorithm, checksum, cancellationToken);
    }

    public Task<FileConcat?> GetUploadConcatAsync(string fileId, CancellationToken cancellationToken)
    {
        return _Inner<ITusConcatenationStore>().GetUploadConcatAsync(fileId, cancellationToken)!;
    }

    public Task<string> CreatePartialFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        return _Inner<ITusConcatenationStore>()
            .CreatePartialFileAsync(uploadLength, _Stamp(metadata), cancellationToken);
    }

    public Task<string> CreateFinalFileAsync(
        string[] partialFiles,
        string? metadata,
        CancellationToken cancellationToken
    )
    {
        return _Inner<ITusConcatenationStore>().CreateFinalFileAsync(partialFiles, _Stamp(metadata), cancellationToken);
    }

    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var file = await _Inner<ITusReadableStore>().GetFileAsync(fileId, cancellationToken).ConfigureAwait(false);

        return file is null ? null : new TusStampedFile(file);
    }

    /// <summary>Returns the client's <paramref name="metadata"/> with the caller's stamp added.</summary>
    /// <exception cref="TusStoreException">
    /// The client used a key that only the server may write. tusdotnet answers a store exception with 400 and its
    /// message.
    /// </exception>
    private string _Stamp(string? metadata)
    {
        if (TusUploadStamp.TryFindReservedKey(metadata, out var key))
        {
            throw new TusStoreException(
                $"Upload-Metadata key \"{key}\" is not allowed: keys that start with "
                    + $"\"{TusUploadStamp.ReservedPrefix}\" are reserved for the server."
            );
        }

        // The endpoint refuses a creating request that has no owner before tusdotnet reaches the store.
        var stamp =
            caller
            ?? throw new InvalidOperationException("An upload cannot be created by a request that has no owner.");

        return stamp.AppendTo(metadata);
    }

    private TCapability _Inner<TCapability>()
        where TCapability : class
    {
        return inner as TCapability
            ?? throw new NotSupportedException(
                $"The upload store {inner.GetType().Name} does not implement {typeof(TCapability).Name}."
            );
    }
}
