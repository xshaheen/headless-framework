// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.MultiTenancy;
using Headless.Tus;
using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Tests.Endpoint;

/// <summary>
/// A store that records which uploads were handed off, which tusdotnet's disk store cannot. It keeps its uploads
/// in a disk store and the record in memory, so it lasts as long as the host does. It can also create, delete,
/// and return an upload, and it reads an upload's bytes only from a stream.
/// </summary>
internal sealed class HandoffDiskStore(TusDiskStore inner)
    : ITusStore,
        ITusCreationStore,
        ITusTerminationStore,
        ITusReadableStore,
        ITusHandoffStore
{
    private readonly ConcurrentDictionary<string, bool> _handedOff = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs once an append has stored its bytes, before the store answers the append. A scenario uses it to act
    /// between an upload's last byte and what the endpoint does next.
    /// </summary>
    public Func<CancellationToken, Task>? AfterAppend { get; set; }

    /// <summary>Makes every read of an upload's handoff state fail, the way an unreachable backend does.</summary>
    public bool FailsHandoffReads { get; set; }

    /// <summary>Makes every delete fail, the way an unreachable backend does.</summary>
    public bool FailsDeletes { get; set; }

    /// <summary>The ambient tenant of every delete, in order.</summary>
    public ConcurrentQueue<string?> DeleteTenants { get; } = new();

    public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        var appended = await inner.AppendDataAsync(fileId, stream, cancellationToken);

        if (AfterAppend is { } afterAppend)
        {
            await afterAppend(cancellationToken);
        }

        return appended;
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

    public Task<string> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.GetUploadMetadataAsync(fileId, cancellationToken);
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        DeleteTenants.Enqueue(AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId);

        if (FailsDeletes)
        {
            throw new IOException("The store is unreachable.");
        }

        await inner.DeleteFileAsync(fileId, cancellationToken);
        _handedOff.TryRemove(fileId, out _);
    }

    public Task<ITusFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        return inner.GetFileAsync(fileId, cancellationToken);
    }

    public async Task<TusHandoffState> GetHandoffStateAsync(string fileId, CancellationToken cancellationToken)
    {
        if (FailsHandoffReads)
        {
            throw new IOException("The store is unreachable.");
        }

        if (!await inner.FileExistAsync(fileId, cancellationToken))
        {
            return TusHandoffState.NotComplete;
        }

        if (_handedOff.ContainsKey(fileId))
        {
            return TusHandoffState.HandedOff;
        }

        var length = await inner.GetUploadLengthAsync(fileId, cancellationToken);
        var offset = await inner.GetUploadOffsetAsync(fileId, cancellationToken);

        return length == offset ? TusHandoffState.Pending : TusHandoffState.NotComplete;
    }

    public async Task MarkHandedOffAsync(string fileId, CancellationToken cancellationToken)
    {
        if (await inner.FileExistAsync(fileId, cancellationToken))
        {
            _handedOff.TryAdd(fileId, true);
        }
    }
}
