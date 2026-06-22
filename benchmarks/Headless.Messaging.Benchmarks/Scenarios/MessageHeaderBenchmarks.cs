// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Isolates F-14 — the per-dispatch header copy. <see cref="MessageHeader"/> wraps a fresh
/// <see cref="Dictionary{TKey,TValue}"/> copied from the origin headers at construction; this happens once
/// per consume dispatch (× consumer fan-out). Allocation scales with <see cref="HeaderCount"/>.
/// </summary>
[MemoryDiagnoser]
public class MessageHeaderBenchmarks
{
    private Dictionary<string, string?> _headers = null!;

    [Params(4, 8, 16)]
    public int HeaderCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var i = 0; i < HeaderCount; i++)
        {
            _headers[$"header-{i}"] = $"value-{i}";
        }
    }

    [Benchmark]
    public MessageHeader Construct() => new(_headers);
}
