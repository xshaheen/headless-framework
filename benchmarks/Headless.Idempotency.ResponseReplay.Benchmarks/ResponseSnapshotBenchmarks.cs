// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Headless.Idempotency;
using Headless.Serializer;

namespace Headless.Idempotency.ResponseReplay.Benchmarks;

/// <summary>
/// Measures the capture → frame → decode pipeline of the stored response at the plan's candidate sizes. The
/// Legacy* benchmarks replicate the previous shape (JSON snapshot with a base64 body over an unpooled doubling
/// buffer) inside this project so the old and new paths compare within one run; they are shape-faithful, not the
/// old source verbatim.
/// </summary>
[MemoryDiagnoser]
public class ResponseSnapshotBenchmarks
{
    private byte[] _body = null!;
    private byte[] _frame = null!;

    [Params(1024, 256 * 1024)]
    public int BodySize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _body = GC.AllocateUninitializedArray<byte>(BodySize);
        new Random(42).NextBytes(_body);

        using var writer = new PooledByteBufferWriter(BodySize + 256);
        IdempotencyResponseSnapshot.WriteTo(writer, 201, [], _body);
        _frame = writer.WrittenSpan.ToArray();
    }

    // ── current path ─────────────────────────────────────────────────────────

    [Benchmark(Baseline = true)]
    public byte[] CaptureAndFrame()
    {
        // The middleware's path: pool-backed capture, then the frame written from the captured span.
        using var capture = new CaptureStream(Stream.Null, int.MaxValue);
        capture.Write(_body);
        capture.Flush();

        using var frame = new PooledByteBufferWriter(capture.CapturedSpan.Length + 256);
        IdempotencyResponseSnapshot.WriteTo(frame, 201, [], capture.CapturedSpan);

        return frame.WrittenSpan.ToArray();
    }

    [Benchmark]
    public int DecodeFrame()
    {
        var snapshot = IdempotencyResponseSnapshot.Decode(_frame);

        return snapshot.Body.Length + snapshot.StatusCode;
    }

    // ── previous shape: JSON snapshot with a base64 body over a doubling buffer ──

    private sealed class LegacySnapshot
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string[]> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; } = [];
    }

    [Benchmark]
    public byte[] LegacyCaptureAndSerialize()
    {
        // MemoryStream grows by doubling and ToArray copies out; STJ then base64-encodes the body into JSON text.
        using var capture = new MemoryStream();
        capture.Write(_body, 0, _body.Length);
        var body = capture.ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(new LegacySnapshot { StatusCode = 201, Body = body });
    }

    [Benchmark]
    public int LegacyDeserialize()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new LegacySnapshot { StatusCode = 201, Body = _body });
        var snapshot = JsonSerializer.Deserialize<LegacySnapshot>(json)!;

        return snapshot.Body.Length + snapshot.StatusCode;
    }
}
