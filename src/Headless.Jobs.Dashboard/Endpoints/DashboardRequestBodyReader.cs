// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Headless.Jobs.Endpoints;

internal static class DashboardRequestBodyReader
{
    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(
        HttpContext context,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken
    )
    {
        if (context.Request.ContentLength > DashboardOptionsBuilder.MaxRequestBodyBytes)
        {
            return (default, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }

        try
        {
            using var limitedBody = new SizeLimitedReadStream(
                context.Request.Body,
                DashboardOptionsBuilder.MaxRequestBodyBytes
            );
            var value = await JsonSerializer
                .DeserializeAsync<T>(limitedBody, options, cancellationToken)
                .ConfigureAwait(false);

            return value is null ? (default, Results.BadRequest("A JSON request body is required.")) : (value, null);
        }
        catch (RequestBodyTooLargeException)
        {
            return (default, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }
        catch (JsonException)
        {
            return (default, Results.BadRequest("The JSON request body is invalid."));
        }
    }

    internal sealed class RequestBodyTooLargeException : IOException { }

    internal sealed class SizeLimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            _Track(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _Track(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            // The request pipeline owns the underlying body stream.
            base.Dispose(disposing);
        }

        private void _Track(int read)
        {
            _bytesRead += read;
            if (_bytesRead > maxBytes)
            {
                throw new RequestBodyTooLargeException();
            }
        }
    }
}
