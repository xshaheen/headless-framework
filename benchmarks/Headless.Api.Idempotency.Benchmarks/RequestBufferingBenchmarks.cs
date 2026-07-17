// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Idempotency.Benchmarks;

/// <summary>
/// Measures the memory-versus-temporary-file trade-off for request buffering at the candidate
/// thresholds from the performance plan. Compare baseline and candidate commits in separate
/// process launches; do not infer a production default from a single run.
/// </summary>
[MemoryDiagnoser]
public class RequestBufferingBenchmarks
{
    private const int _PreviousCompatibilityThreshold = (1024 * 1024) + 1;
    private byte[] _payload = null!;

    [Params(4 * 1024, 1024 * 1024)]
    public int BodySize { get; set; }

    [Params(30 * 1024, 64 * 1024, 128 * 1024, _PreviousCompatibilityThreshold)]
    public int BufferThreshold { get; set; }

    [Params(1, 32, 128)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = GC.AllocateUninitializedArray<byte>(BodySize);
        new Random(42).NextBytes(_payload);
    }

    [Benchmark]
    public async Task<int> BufferAndFingerprintAsync()
    {
        if (Concurrency == 1)
        {
            return await _BufferAndFingerprintOneAsync().ConfigureAwait(false);
        }

        var tasks = new Task<int>[Concurrency];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = _BufferAndFingerprintOneAsync();
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var checksum = 0;
        for (var i = 0; i < results.Length; i++)
        {
            checksum ^= results[i];
        }

        return checksum;
    }

    private async Task<int> _BufferAndFingerprintOneAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new NonSeekableReadStream(_payload);
        context.Request.EnableBuffering(BufferThreshold);

        if (!context.Request.Body.CanSeek)
        {
            throw new InvalidOperationException("EnableBuffering did not install a rewindable request stream.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }

            context.Request.Body.Position = 0;
            return hash.GetCurrentHash()[0];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await context.Request.Body.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class NonSeekableReadStream(byte[] payload) : Stream
    {
        private readonly MemoryStream _inner = new(payload, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
