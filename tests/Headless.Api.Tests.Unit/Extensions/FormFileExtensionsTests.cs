// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Extensions;

public sealed class FormFileExtensionsTests : TestBase
{
    [Fact]
    public async Task should_bound_parallel_file_streams_when_save_many()
    {
        // given
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tracker = new StreamConcurrencyTracker();
        var files = Enumerable
            .Range(0, Environment.ProcessorCount + 4)
            .Select(index => new TrackingFormFile($"file-{index}.txt", tracker))
            .ToArray<IFormFile>();

        try
        {
            // when
            var results = await files.SaveAsync(directory, AbortToken);

            // then
            results.Should().OnlyContain(result => result.IsSuccess);
            tracker.MaxActive.Should().BeLessThanOrEqualTo(Environment.ProcessorCount);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class TrackingFormFile(string fileName, StreamConcurrencyTracker tracker) : IFormFile
    {
        public string ContentType => "text/plain";

        public string ContentDisposition => "";

        public IHeaderDictionary Headers { get; } = new HeaderDictionary();

        public long Length => 1;

        public string Name => fileName;

        public string FileName => fileName;

        public Stream OpenReadStream()
        {
            return new TrackingReadStream(tracker);
        }

        public void CopyTo(Stream target)
        {
            throw new NotSupportedException();
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            await using var stream = OpenReadStream();
            await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TrackingReadStream : Stream
    {
        private readonly StreamConcurrencyTracker _tracker;
        private bool _read;

        public TrackingReadStream(StreamConcurrencyTracker tracker)
        {
            _tracker = tracker;
            _tracker.Open();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 1;

        public override long Position
        {
            get => _read ? 1 : 0;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read)
            {
                return 0;
            }

            Thread.Sleep(50);
            buffer[offset] = (byte)'x';
            _read = true;

            return 1;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_read)
            {
                return 0;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            buffer.Span[0] = (byte)'x';
            _read = true;

            return 1;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            if (_read)
            {
                return 0;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            buffer[offset] = (byte)'x';
            _read = true;

            return 1;
        }

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
            if (disposing)
            {
                _tracker.Close();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StreamConcurrencyTracker
    {
        private int _active;
        private int _maxActive;

        public int MaxActive => _maxActive;

        public void Open()
        {
            var active = Interlocked.Increment(ref _active);

            while (true)
            {
                var observed = _maxActive;
                if (active <= observed || Interlocked.CompareExchange(ref _maxActive, active, observed) == observed)
                {
                    return;
                }
            }
        }

        public void Close()
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
