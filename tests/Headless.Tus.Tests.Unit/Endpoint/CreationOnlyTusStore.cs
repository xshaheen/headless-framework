// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Tests.Endpoint;

/// <summary>
/// A store with tusdotnet's required contract and the creation extension, and nothing else: it cannot delete,
/// expire, defer a length, verify a checksum, or return a file, and it reads an upload's bytes only from a stream.
/// It keeps its uploads in a disk store.
/// </summary>
internal class CreationOnlyTusStore(TusDiskStore inner) : ITusStore, ITusCreationStore
{
    public Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        return inner.AppendDataAsync(fileId, stream, cancellationToken);
    }

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

    public Task<string> CreateFileAsync(long uploadLength, string metadata, CancellationToken cancellationToken)
    {
        return inner.CreateFileAsync(uploadLength, metadata, cancellationToken);
    }

    public virtual Task<string> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.GetUploadMetadataAsync(fileId, cancellationToken);
    }
}
