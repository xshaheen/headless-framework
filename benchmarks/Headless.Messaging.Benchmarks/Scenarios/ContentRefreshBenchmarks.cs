// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// CPU and allocation cost of the envelope re-serialization that a <see cref="Persistence.MessageContentWrite.Refresh"/>
/// transition performs before the row write. The row-write half is measured against PostgreSQL by the
/// <c>relay</c> scenario.
/// </summary>
[MemoryDiagnoser]
public class ContentRefreshBenchmarks
{
    private JsonUtf8Serializer _serializer = null!;
    private Message _message = null!;

    [Params(1024, 65536)]
    public int PayloadBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _serializer = new JsonUtf8Serializer(Options.Create(new MessagingOptions { Version = "benchmark" }));
        _message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "0198a2b4-0000-7000-8000-000000000001",
                [Headers.MessageName] = "benchmark.order.placed",
                [Headers.SentTime] = "2026-10-14T10:00:00.0000000+00:00",
                [Headers.ExecutionInstanceId] = "host-1",
            },
            new OrderPlaced(new string('x', PayloadBytes))
        );
    }

    [Benchmark]
    public string SerializeEnvelope() => _serializer.Serialize(_message);

    private sealed record OrderPlaced(string Body);
}
