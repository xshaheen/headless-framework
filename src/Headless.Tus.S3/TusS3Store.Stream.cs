// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// Behaves exactly like the <c>PipeReader</c> overload. The new offset covers every byte stored, including bytes
    /// received before a disconnect. When storage refuses a part, its bytes and every later byte are stored as tail
    /// segments instead; only bytes storage refuses outright are not counted.
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
            await using var writer = await _BeginAppendAsync(fileId, cancellationToken).ConfigureAwait(false);

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
}
