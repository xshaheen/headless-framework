// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using Amazon.S3;
using Headless.Checks;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Headless.Tus;

public sealed partial class TusS3Store
{
    /// <summary>
    /// Reads a PATCH body from a <c>PipeReader</c> and stores it as equal-size parts and a short tail, returning the
    /// number of bytes this request durably appended.
    /// </summary>
    /// <param name="fileId">the TUS file identifier</param>
    /// <param name="pipeReader">the pipeline reader supplying the PATCH request body</param>
    /// <param name="cancellationToken">
    /// the request token; it stops only the reading of the body, because tusdotnet cancels it when the client
    /// disconnects and the bytes received by then are still stored, within <see cref="TusS3StoreOptions.PersistTimeout"/>
    /// </param>
    /// <returns>bytes appended by this PATCH request</returns>
    /// <remarks>
    /// Behaves exactly like the <c>Stream</c> overload. A cancelled read is a disconnect: the bytes it hands over are
    /// stored before the reader stops.
    /// </remarks>
    /// <exception cref="TusStoreException">
    /// The upload does not exist, is gone or complete, or the body runs past the upload length.
    /// </exception>
    /// <exception cref="AmazonS3Exception">
    /// Another request wrote the upload's state after this one read it (412, 409, or 404), so nothing this request
    /// wrote is recorded (see the <c>TusS3Store</c> remarks); or storage refused bytes, after every byte before them
    /// was recorded.
    /// </exception>
    public async Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(pipeReader);
        await _EnsureValidFileIdAsync(fileId).ConfigureAwait(false);

        ReadResult result = default;

        try
        {
            await using var writer = await _BeginAppendAsync(fileId, cancellationToken).ConfigureAwait(false);

            do
            {
                try
                {
                    result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    writer.OnClientDisconnected();

                    break;
                }

                var keepReading = await _CopyAsync(result.Buffer, writer).ConfigureAwait(false);

                // Every byte was copied into the writer's own buffers, so all of it is consumed: once the client is
                // gone, bytes left in the pipe could not be read again.
                pipeReader.AdvanceTo(result.Buffer.End);

                if (!keepReading)
                {
                    break;
                }

                if (result.IsCanceled || cancellationToken.IsCancellationRequested)
                {
                    writer.OnClientDisconnected();

                    break;
                }
            } while (!result.IsCompleted);

            await pipeReader.CompleteAsync().ConfigureAwait(false);

            return await _CommitAppendAsync(fileId, writer).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogS3AppendFailed(e, fileId);

            // Completing the reader keeps Kestrel from reporting the abandoned body as a connection abort.
            try
            {
                pipeReader.AdvanceTo(result.Buffer.End);
                await pipeReader.CompleteAsync().ConfigureAwait(false);
            }
#pragma warning disable ERP022 // A cleanup fault must not replace the exception that is already propagating.
            catch
            {
                // The reader may already be completed or advanced; the original failure is what the caller needs.
            }
#pragma warning restore ERP022

            throw;
        }
    }

    // Copies one read into the writer's part buffers.
    private static async ValueTask<bool> _CopyAsync(ReadOnlySequence<byte> source, TusS3PartWriter writer)
    {
        while (!source.IsEmpty)
        {
            var destination = writer.GetBuffer();
            var take = (int)Math.Min(source.Length, destination.Length);
            source.Slice(0, take).CopyTo(destination.Span);
            source = source.Slice(take);

            if (!await writer.AdvanceAsync(take).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }
}
