// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

// The baseline methods call reflection-based System.Text.Json overloads directly to reproduce the pre-redesign
// Stream path. Those overloads are annotated RequiresUnreferencedCode/RequiresDynamicCode; this is a benchmark
// exe (never trimmed/AOT-published), so the analyzer warnings are intentionally suppressed.
#pragma warning disable IL2026, IL3050

namespace Headless.Serializer.Benchmarks;

/// <summary>
/// Representative payload — a handful of scalars, a small string collection, and a dictionary — sized like a
/// typical cache entry or message envelope.
/// </summary>
public sealed class SerializerPayload
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required List<string> Tags { get; init; }

    public required Dictionary<string, int> Metrics { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public static SerializerPayload Sample()
    {
        return new SerializerPayload
        {
            Id = 42,
            Name = "headless-framework",
            Description = "A modular .NET framework for building APIs and backend services with zero lock-in.",
            Tags = ["alpha", "beta", "gamma", "delta"],
            Metrics = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["reads"] = 1000,
                ["writes"] = 250,
                ["evictions"] = 12,
            },
            CreatedAt = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero),
        };
    }
}

/// <summary>Serialize-to-bytes: the old <see cref="MemoryStream"/> + <c>ToArray()</c> path vs the buffer-first path.</summary>
public class SerializeBenchmarks
{
    private readonly SystemJsonSerializer _serializer = new();
    private readonly JsonSerializerOptions _options = JsonConstants.DefaultWebJsonOptions;
    private SerializerPayload _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload = SerializerPayload.Sample();
    }

    [Benchmark(Baseline = true, Description = "Stream + ToArray (old)")]
    public byte[] OldStreamPath()
    {
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, _payload, _options);
        return stream.ToArray();
    }

    [Benchmark(Description = "Buffer-first SerializeToBytes (new)")]
    public byte[]? NewBufferPath()
    {
        return _serializer.SerializeToBytes(_payload);
    }
}

/// <summary>Deserialize-from-bytes: the old <see cref="MemoryStream"/> wrapper vs the in-place buffer read.</summary>
public class DeserializeBenchmarks
{
    private readonly SystemJsonSerializer _serializer = new();
    private readonly JsonSerializerOptions _options = JsonConstants.DefaultWebJsonOptions;
    private byte[] _bytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _bytes = JsonSerializer.SerializeToUtf8Bytes(SerializerPayload.Sample(), _options);
    }

    [Benchmark(Baseline = true, Description = "new MemoryStream + Deserialize (old)")]
    public SerializerPayload? OldStreamPath()
    {
        using var stream = new MemoryStream(_bytes);
        return JsonSerializer.Deserialize<SerializerPayload>(stream, _options);
    }

    [Benchmark(Description = "Buffer-first Deserialize<T>(byte[]) (new)")]
    public SerializerPayload? NewBufferPath()
    {
        return _serializer.Deserialize<SerializerPayload>(_bytes);
    }
}
