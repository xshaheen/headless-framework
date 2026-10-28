// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net;
using System.Runtime.ExceptionServices;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// Turns the bytes of one PATCH into equal-size parts and a short tail, then records them with one conditional state
/// write. Both append overloads feed this one writer, so the <c>Stream</c> and <c>PipeReader</c> paths store the same
/// bytes the same way.
/// </summary>
/// <remarks>
/// <para>
/// The bytes the upload already holds in tail segments come first. While they and the new bytes stay below one part,
/// the new bytes become one more segment and nothing is read back. Once they fill a part, the segments are read into
/// the front of the first part buffer, and every full part is uploaded under a part number reserved beforehand. The
/// bytes left over become one new segment. The state write that records the new parts and segments is the commit; the
/// consumed segments are deleted only after it, so a crash at any step leaves the old or the new state readable.
/// </para>
/// <para>
/// Memory is bounded: at most <see cref="TusS3StoreOptions.MaxConcurrentPartUploads"/> part buffers are uploading or
/// waiting for an earlier part, plus the one being filled. Parts are released in order, because a part that fails
/// turns itself and every later part into tail segments, so their bytes must still be in hand.
/// </para>
/// <para>
/// Storage calls never observe the request token: tusdotnet cancels it when the client disconnects, which is exactly
/// when the bytes already received must still be stored. They observe the writer's own token, which
/// <see cref="OnClientDisconnected"/> arms to fire after <see cref="TusS3StoreOptions.PersistTimeout"/>.
/// </para>
/// </remarks>
internal sealed class TusS3PartWriter : IAsyncDisposable
{
    private readonly TusS3AppendServices _services;
    private readonly string _fileId;
    private readonly TusS3UploadState _state;
    private readonly int _partSize;
    private readonly long _startOffset;
    private readonly long _tailStart;
    private readonly long _tailLength;
    private readonly long _maxAppendBytes;
    private readonly List<TusS3CommittedSegment> _oldSegments;
    private readonly CancellationTokenSource _persistCts = new();
    private readonly Queue<PendingPart> _uploading = new();
    private readonly List<HeldBuffer> _fallback = [];
    private readonly List<TusS3CommittedPart> _committedParts = [];
    private readonly List<TusS3CommittedSegment> _newSegments = [];

    private TusS3LoadedUpload _upload;
    private TusS3PartNumberWindow? _window;
    private byte[]? _filling;
    private int _fillCount;
    private long _fillingStart;
    private bool _prefixPending;
    private int _highestAttemptedPart;
    private bool _partFailed;
    private long _appendedBytes;
    private ExceptionDispatchInfo? _halt;

    public TusS3PartWriter(TusS3AppendServices services, string fileId, TusS3LoadedUpload upload)
    {
        _services = services;
        _fileId = fileId;
        _upload = upload;
        _state = upload.State ?? throw new InvalidOperationException("A completed upload accepts no appends.");
        _partSize = _state.PartSize;
        _startOffset = _state.Offset;
        _oldSegments = [.. _state.Segments];
        _tailLength = _oldSegments.Sum(segment => segment.Length);
        _tailStart = _startOffset - _tailLength;
        _fillingStart = _tailStart;

        var limit = _state.UploadLength ?? TusS3PartSize.DeferredLengthCap(_partSize, services.Options.PartClaimWindow);
        _maxAppendBytes = limit - _startOffset;
    }

    /// <summary>
    /// Set when storage refused bytes and the writer stopped taking more: the caller stops reading the body and commits
    /// what is durable, which then rethrows the refusal.
    /// </summary>
    public bool Halted => _halt is not null;

    private CancellationToken StorageToken => _persistCts.Token;

    /// <summary>
    /// Prepares the buffers before the body is read. A tail of at least one part is read back first, because the part
    /// it fills is uploaded whatever the body holds; a shorter tail is read only once the body fills the part.
    /// </summary>
    public async Task StartAsync()
    {
        if (_tailLength == 0)
        {
            return;
        }

        if (_tailLength < _partSize)
        {
            // Leave room for the tail at the front of the first part; it is read in only if the part fills.
            _filling = _services.PartPool.Rent(_partSize);
            _fillCount = (int)_tailLength;
            _prefixPending = true;

            return;
        }

        foreach (var segment in _oldSegments)
        {
            var copied = 0L;

            while (copied < segment.Length && !Halted)
            {
                var memory = GetBuffer();
                var take = (int)Math.Min(memory.Length, segment.Length - copied);
                await _ReadSegmentAsync(segment, copied, memory[..take]).ConfigureAwait(false);
                copied += take;
                await _AdvanceAsync(take).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Returns free space in the part being filled. Never empty while the writer is not halted, because a full part is
    /// handed off before the next write.
    /// </summary>
    public Memory<byte> GetBuffer()
    {
        if (_filling is null)
        {
            _filling = _services.PartPool.Rent(_partSize);
            _fillCount = 0;
        }

        return _filling.AsMemory(_fillCount, _partSize - _fillCount);
    }

    /// <summary>Records that the caller wrote <paramref name="count"/> body bytes into the last buffer it was given.</summary>
    /// <returns><see langword="false"/> when storage refused bytes and the caller must stop reading the body</returns>
    /// <exception cref="TusStoreException">The body runs past the upload's length or the deferred-length cap.</exception>
    public async ValueTask<bool> AdvanceAsync(int count)
    {
        if (_appendedBytes + count > _maxAppendBytes)
        {
            throw new TusStoreException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Request contains more data than the file's upload length. Request data: {_startOffset + _appendedBytes + count}, upload length: {_startOffset + _maxAppendBytes}."
                )
            );
        }

        _appendedBytes += count;
        await _AdvanceAsync(count).ConfigureAwait(false);

        return !Halted;
    }

    /// <summary>
    /// Bounds the rest of the work by <see cref="TusS3StoreOptions.PersistTimeout"/>: the client is gone, so the writer
    /// stores what it received, but must not outlive the lock that keeps a second writer away.
    /// </summary>
    public void OnClientDisconnected()
    {
        _services.Logger.LogS3ClientDisconnected(_fileId, _appendedBytes);
        _persistCts.CancelAfter(_services.Options.PersistTimeout);
    }

    /// <summary>
    /// Writes the remaining bytes, records every durable byte with one conditional state write, then deletes the tail
    /// segments the new parts or segments replaced.
    /// </summary>
    /// <returns>the upload as committed and the number of bytes this request appended</returns>
    /// <exception cref="AmazonS3Exception">
    /// Another request changed the state after this one read it (see <c>TusS3StateStore.UpdateAsync</c>), or storage
    /// refused bytes; in the second case every byte before the refused ones is committed first.
    /// </exception>
    public async Task<(TusS3LoadedUpload Upload, long Appended)> CommitAsync()
    {
        await _DrainUploadsAsync().ConfigureAwait(false);
        await _StoreRemainderAsync().ConfigureAwait(false);
        await _FlushFallbackAsync().ConfigureAwait(false);

        var partsEnd = _tailStart + _committedParts.Sum(part => part.Size);
        var durableEnd = _newSegments.Count > 0 ? _newSegments[^1].Offset + _newSegments[^1].Length : partsEnd;

        // Nothing past the old offset landed (an empty body, or storage refused the first bytes), so the old state
        // stays as it is: its counter already lies past every part number this request reserved.
        if (durableEnd <= _startOffset)
        {
            _halt?.Throw();

            return (_upload, 0);
        }

        // Only a tail-only append that did not fold the old segments keeps them; any other durable range starts at
        // the end of the parts, which the old segments preceded.
        var keepOld = _committedParts.Count == 0 && _newSegments.Count > 0 && _newSegments[0].Offset == _startOffset;
        var consumed = keepOld ? [] : _oldSegments;

        _state.Parts.AddRange(_committedParts);
        _state.Segments = keepOld ? [.. _oldSegments, .. _newSegments] : [.. _newSegments];
        _state.Offset = durableEnd;

        if (durableEnd == _state.UploadLength)
        {
            // The commit that stores the last byte is also the completion fence: only the request whose write lands
            // assembles the finished object, and HEAD keeps reporting this PATCH's start offset until that is done, so
            // a client whose final PATCH fails re-sends it.
            _state.Status = TusS3UploadStatus.Completing;
            _state.PreFinalOffset = _startOffset;
            _state.ExpiresAtBeforeCompleting = _state.ExpiresAt;
        }

        if (_window is not null)
        {
            // The commit's If-Match proves no other writer reserved numbers since this request did, so the unused
            // rest of the window goes back. A failed upload may still land late, so its number is never reused.
            var lastCommitted = _committedParts.Count > 0 ? _committedParts[^1].Number : _window.First - 1;
            _state.NextPartNumber = (_partFailed ? Math.Max(lastCommitted, _highestAttemptedPart) : lastCommitted) + 1;
        }

        _upload = await _services.StateStore.UpdateAsync(_fileId, _upload, StorageToken).ConfigureAwait(false);

        _services.Logger.LogS3AppendCommitted(
            _fileId,
            durableEnd - _startOffset,
            _committedParts.Count,
            _newSegments.Count
        );

        await _DeleteSegmentsAsync(consumed).ConfigureAwait(false);

        _halt?.Throw();

        return (_upload, durableEnd - _startOffset);
    }

    public async ValueTask DisposeAsync()
    {
        // A buffer goes back to the pool only once no upload reads it, or the pool would hand out bytes still in
        // flight.
        while (_uploading.TryDequeue(out var pending))
        {
            try
            {
                await pending.Upload.ConfigureAwait(false);
            }
#pragma warning disable ERP022 // The append already failed or committed; a late upload failure changes nothing.
            catch
            {
                // Observed only so it does not surface as an unobserved task exception.
            }
#pragma warning restore ERP022

            _Return(pending.Buffer);
        }

        foreach (var held in _fallback)
        {
            _Return(held.Buffer);
        }

        _fallback.Clear();

        if (_filling is not null)
        {
            _Return(_filling);
            _filling = null;
        }

        _persistCts.Dispose();
    }

    private async ValueTask _AdvanceAsync(int count)
    {
        _fillCount += count;

        if (_fillCount < _partSize || Halted)
        {
            return;
        }

        // Hold at most the configured number of parts in flight; waiting on the oldest also releases parts in order.
        while (!_partFailed && _uploading.Count >= _services.Options.MaxConcurrentPartUploads)
        {
            await _CompleteOldestAsync().ConfigureAwait(false);
        }

        if (_partFailed)
        {
            _fallback.Add(_TakeFilling(_partSize));
            await _FlushFallbackAsync().ConfigureAwait(false);

            return;
        }

        await _DispatchPartAsync(_partSize).ConfigureAwait(false);
    }

    // The full buffer stays the writer's filling buffer until it is handed to an upload, so a failure before then still
    // returns it to the pool on dispose.
    private async Task _DispatchPartAsync(int length)
    {
        int partNumber;

        while (_window is null || !_window.TryTake(out partNumber))
        {
            await _ReserveWindowAsync().ConfigureAwait(false);
        }

        if (_prefixPending)
        {
            // The first part starts with the old tail; read it in now that the part is certain to be uploaded.
            await _LoadPrefixAsync(_filling!).ConfigureAwait(false);
        }

        var held = _TakeFilling(length);
        _highestAttemptedPart = partNumber;
        _uploading.Enqueue(
            new PendingPart(
                partNumber,
                held.Buffer,
                held.Offset,
                length,
                _UploadPartAsync(partNumber, held.Buffer, length)
            )
        );
    }

    private HeldBuffer _TakeFilling(int count, int bufferOffset = 0)
    {
        var held = new HeldBuffer(_filling!, _fillingStart + bufferOffset, count - bufferOffset, bufferOffset);
        _filling = null;
        _fillingStart += _partSize;

        return held;
    }

    private async Task _ReserveWindowAsync()
    {
        string? createdUploadId = null;

        if (_state.MultipartUploadId is null)
        {
            createdUploadId = await _services
                .CreateMultipartUploadAsync(_fileId, _state.Metadata, StorageToken)
                .ConfigureAwait(false);
            _state.MultipartUploadId = createdUploadId;
            _state.MultipartUploadCreatedAt = _services.TimeProvider.GetUtcNow();
        }

        if (!TusS3PartNumberWindow.TryReserve(_state.NextPartNumber, _services.Options.PartClaimWindow, out var window))
        {
            // Crashes and lost fences used every part number this multipart upload has; no later part can be stored.
            _services.Logger.LogS3PartNumbersExhausted(_fileId);
            await _MarkGoneAsync().ConfigureAwait(false);

            throw new TusStoreException($"File {_fileId} has used every part number and can no longer be resumed");
        }

        _state.NextPartNumber = window.End;

        try
        {
            _upload = await _services.StateStore.UpdateAsync(_fileId, _upload, StorageToken).ConfigureAwait(false);
        }
        catch
        {
            if (createdUploadId is not null)
            {
                // The losing request's multipart upload is recorded nowhere, so nothing else would ever abort it.
                await _AbortMultipartUploadAsync(createdUploadId).ConfigureAwait(false);
            }

            throw;
        }

        _window = window;
    }

    private async Task<string> _UploadPartAsync(int partNumber, byte[] buffer, int length)
    {
        await using var body = new MemoryStream(buffer, 0, length, writable: false);

        var request = new UploadPartRequest
        {
            BucketName = _services.Options.Bucket,
            Key = _services.Keys.FinishedObject(_fileId),
            UploadId = _state.MultipartUploadId,
            PartNumber = partNumber,
            InputStream = body,
        };

        TusS3RequestPresets.Apply(request, _services.Options.Provider);

        var response = await _services.S3.UploadPartAsync(request, StorageToken).ConfigureAwait(false);

        return response.ETag;
    }

    // Waits for the oldest part in flight. A success is committed and its buffer released; a failure turns that part
    // and every later one into tail segments, because only a contiguous run of parts can be committed.
    private async Task _CompleteOldestAsync()
    {
        var oldest = _uploading.Dequeue();

        try
        {
            var etag = await oldest.Upload.ConfigureAwait(false);
            _committedParts.Add(new TusS3CommittedPart(oldest.Number, etag, oldest.Length));
            _Return(oldest.Buffer);

            return;
        }
        catch (AmazonS3Exception e) when (string.Equals(e.ErrorCode, "NoSuchUpload", StringComparison.Ordinal))
        {
            _Return(oldest.Buffer);
            _services.Logger.LogS3MultipartUploadMissing(_fileId);
            await _MarkGoneAsync().ConfigureAwait(false);

            throw new TusStoreException($"File {_fileId} does not exist: its multipart upload is gone");
        }
        catch (Exception e)
        {
            _services.Logger.LogS3PartUploadFailed(e, _fileId, oldest.Number);
            _partFailed = true;
            _fallback.Add(new HeldBuffer(oldest.Buffer, oldest.Start, oldest.Length));
        }

        while (_uploading.TryDequeue(out var later))
        {
            try
            {
                await later.Upload.ConfigureAwait(false);
            }
#pragma warning disable ERP022 // A later part is discarded whether it landed or not: its bytes become a tail segment.
            catch
            {
                // Its bytes are kept below, so its outcome does not matter.
            }
#pragma warning restore ERP022

            _fallback.Add(new HeldBuffer(later.Buffer, later.Start, later.Length));
        }
    }

    private async Task _DrainUploadsAsync()
    {
        while (_uploading.Count > 0 && !_partFailed)
        {
            await _CompleteOldestAsync().ConfigureAwait(false);
        }
    }

    // The bytes after the last full part. In a tail-only append they normally become one new segment after the old
    // ones; when that would exceed the segment limit, the old segments are read back and folded into it.
    private async Task _StoreRemainderAsync()
    {
        if (_filling is null)
        {
            return;
        }

        var count = _fillCount;

        if (_IsFinalShortPart(count))
        {
            // The upload's last bytes go straight into its last part, which may be shorter than the others, so the
            // completion that follows only has to complete the multipart upload instead of reading a tail back first.
            if (_prefixPending)
            {
                await _LoadPrefixAsync(_filling).ConfigureAwait(false);
            }

            await _DispatchPartAsync(count).ConfigureAwait(false);
            await _DrainUploadsAsync().ConfigureAwait(false);

            return;
        }

        if (_prefixPending)
        {
            if (count == _tailLength)
            {
                return;
            }

            if (_oldSegments.Count + 1 <= _services.Options.MaxTailSegments)
            {
                _fallback.Add(_TakeFilling(count, bufferOffset: (int)_tailLength));

                return;
            }

            await _LoadPrefixAsync(_filling).ConfigureAwait(false);
        }

        if (count > 0)
        {
            _fallback.Add(_TakeFilling(count));
        }
    }

    // The bytes in hand end the upload and its parts already go to a multipart upload, so they can be its last part.
    // After a part failed, the bytes go to segments instead, because only a contiguous run of parts can be committed.
    private bool _IsFinalShortPart(int count)
    {
        var newBytes = _prefixPending ? count - _tailLength : count;

        return newBytes > 0
            && !_partFailed
            && _halt is null
            && _state.MultipartUploadId is not null
            && _state.UploadLength == _fillingStart + count;
    }

    // Writes the held buffers as tail segments, in order, stopping at the first one storage refuses so the committed
    // tail stays contiguous.
    private async Task _FlushFallbackAsync()
    {
        while (_fallback.Count > 0)
        {
            var held = _fallback[0];
            _fallback.RemoveAt(0);

            try
            {
                if (_halt is null)
                {
                    _newSegments.Add(await _PutSegmentAsync(held).ConfigureAwait(false));
                }
            }
            catch (Exception e)
            {
                _services.Logger.LogS3SegmentWriteFailed(e, _fileId, held.Offset);
                _halt = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                _Return(held.Buffer);
            }
        }
    }

    private async Task<TusS3CommittedSegment> _PutSegmentAsync(HeldBuffer held)
    {
        var key = _services.Keys.Segment(
            _fileId,
            held.Offset,
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );
        await using var body = new MemoryStream(held.Buffer, held.BufferOffset, held.Length, writable: false);

        var request = new PutObjectRequest
        {
            BucketName = _services.Options.Bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
        };

        TusS3RequestPresets.Apply(request, _services.Options.Provider);

        await _services.S3.PutObjectAsync(request, StorageToken).ConfigureAwait(false);

        return new TusS3CommittedSegment(key, held.Offset, held.Length);
    }

    private async Task _LoadPrefixAsync(byte[] buffer)
    {
        var position = 0;

        foreach (var segment in _oldSegments)
        {
            await _ReadSegmentAsync(segment, 0, buffer.AsMemory(position, (int)segment.Length)).ConfigureAwait(false);
            position += (int)segment.Length;
        }

        _prefixPending = false;
    }

    // Reads part of a committed segment by the range its recorded length allows, so a segment object that somehow
    // holds more bytes cannot shift the upload's content.
    private Task _ReadSegmentAsync(TusS3CommittedSegment segment, long from, Memory<byte> destination)
    {
        return ReadSegmentAsync(_services, segment, from, destination, StorageToken);
    }

    /// <summary>
    /// Reads <paramref name="destination"/>'s length of a committed segment, starting <paramref name="from"/> bytes
    /// into it.
    /// </summary>
    /// <exception cref="InvalidDataException">The segment object holds fewer bytes than its upload recorded.</exception>
    public static async Task ReadSegmentAsync(
        TusS3AppendServices services,
        TusS3CommittedSegment segment,
        long from,
        Memory<byte> destination,
        CancellationToken cancellationToken
    )
    {
        if (destination.IsEmpty)
        {
            return;
        }

        var request = new GetObjectRequest
        {
            BucketName = services.Options.Bucket,
            Key = segment.Key,
            ByteRange = new ByteRange(from, from + destination.Length - 1),
        };

        using var response = await services.S3.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        await using var content = response.ResponseStream;
        var read = 0;

        while (read < destination.Length)
        {
            var n = await content.ReadAsync(destination[read..], cancellationToken).ConfigureAwait(false);

            if (n == 0)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tail segment '{segment.Key}' holds fewer bytes than the {segment.Length} its upload recorded."
                    )
                );
            }

            read += n;
        }
    }

    private async Task _DeleteSegmentsAsync(List<TusS3CommittedSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        try
        {
            var request = new DeleteObjectsRequest
            {
                BucketName = _services.Options.Bucket,
                Objects = [.. segments.Select(segment => new KeyVersion { Key = segment.Key })],
                Quiet = true,
            };

            await _services.S3.DeleteObjectsAsync(request, StorageToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The commit already landed; a leftover segment only costs storage until the upload is removed.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _services.Logger.LogS3SegmentCleanupFailed(e, _fileId, segments.Count);
        }
    }

    private async Task _MarkGoneAsync()
    {
        _state.Status = TusS3UploadStatus.Gone;

        try
        {
            _upload = await _services.StateStore.UpdateAsync(_fileId, _upload, StorageToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception e) when (TusS3StateStore.IsStaleWrite(e) || e.StatusCode is HttpStatusCode.NotFound)
        {
            // Another request already moved the upload on; it will meet the same missing parts itself.
        }
    }

    private async Task _AbortMultipartUploadAsync(string uploadId)
    {
        try
        {
            var request = new AbortMultipartUploadRequest
            {
                BucketName = _services.Options.Bucket,
                Key = _services.Keys.FinishedObject(_fileId),
                UploadId = uploadId,
            };

            // Not the writer's token: the abort must run even when the request failed because that token fired.
            await _services.S3.AbortMultipartUploadAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best effort: the claim failure is what the caller must see, and the sweep collects a leak.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _services.Logger.LogS3MultipartAbortFailed(e, _fileId);
        }
    }

    private void _Return(byte[] buffer)
    {
        // clearArray: the buffer held upload bytes that must not reach another pool user.
        _services.PartPool.Return(buffer, clearArray: true);
    }

    private sealed record PendingPart(int Number, byte[] Buffer, long Start, int Length, Task<string> Upload);

    private sealed record HeldBuffer(byte[] Buffer, long Offset, int Length, int BufferOffset = 0);
}
