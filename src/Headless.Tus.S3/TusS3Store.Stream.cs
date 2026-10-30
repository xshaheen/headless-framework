// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Amazon.S3;
using Headless.Checks;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Reads a PATCH body from a <c>Stream</c> and stores it as equal-size parts and a short tail, returning the number
    /// of bytes this request durably appended.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="stream">the stream supplying the PATCH request body</param>
    /// <param name="cancellationToken">
    /// the request token; it stops only the reading of the body, because tusdotnet cancels it when the client
    /// disconnects and the bytes received by then are still stored, within <see cref="TusS3StoreOptions.PersistTimeout"/>
    /// </param>
    /// <returns>bytes appended by this PATCH request</returns>
    /// <remarks>
    /// The <c>Stream</c> and <c>PipeReader</c> overloads store a body the same way. The PATCH that stores the last byte
    /// also assembles the finished object. A PATCH to an upload whose bytes are all stored but whose completion failed
    /// reads and discards its body, which the client re-sent, and finishes the completion. The new offset covers every
    /// byte stored, including bytes received before a disconnect. When storage refuses a part, its bytes and every later
    /// byte are stored as tail segments instead; only bytes storage refuses outright are not counted.
    /// </remarks>
    /// <exception cref="TusStoreException">
    /// The upload does not exist, is gone or complete, or the body runs past the upload length.
    /// </exception>
    /// <exception cref="AmazonS3Exception">
    /// Another request wrote the upload's state after this one read it (412, 409, or 404), so nothing this request
    /// wrote is recorded (see the <c>TusS3Store</c> remarks); or storage refused bytes, after every byte before them
    /// was recorded.
    /// </exception>
    public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(stream);
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        try
        {
            var upload = await _LoadForAppendAsync(fileId, cancellationToken).ConfigureAwait(false);

            if (!_AcceptsBytes(upload))
            {
                var bodyBytes = await _DrainAsync(stream, cancellationToken).ConfigureAwait(false);

                return await _AppendToFullUploadAsync(fileId, upload, bodyBytes).ConfigureAwait(false);
            }

            await using var writer = await _BeginAppendAsync(fileId, upload).ConfigureAwait(false);

            while (true)
            {
                var buffer = writer.GetBuffer();
                int read;

                try
                {
                    read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // A read can surface the disconnect as a cancellation instead of an end of body.
                    writer.OnClientDisconnected();

                    break;
                }

                // The bytes of the read that saw the disconnect are kept before the loop stops.
                if (read > 0 && !await writer.AdvanceAsync(read).ConfigureAwait(false))
                {
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    writer.OnClientDisconnected();

                    break;
                }

                if (read == 0)
                {
                    break;
                }
            }

            return await _CommitAppendAsync(fileId, writer).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogS3AppendFailed(e, fileId);

            throw;
        }
    }

    private static async Task<long> _DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var total = 0L;

        try
        {
            int read;

            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away; nothing it sent is kept, so there is nothing more to read.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return total;
    }
}
