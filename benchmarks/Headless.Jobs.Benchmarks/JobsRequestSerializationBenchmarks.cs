// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Jobs.Benchmarks;

public class JobsRequestSerializationBenchmarks
{
    private byte[] _request = null!;
    private JobsRequestSerializationOptions _serializationOptions = null!;

    [Params(256, 4 * 1024, 64 * 1024, 1024 * 1024)]
    public int PayloadSize { get; set; }

    [Params(false, true)]
    public bool Compressed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _serializationOptions = new JobsRequestSerializationOptions { UseGZipCompression = Compressed };
        _request = JobsHelper.CreateJobRequest(
            new BenchmarkPayload(new string('x', PayloadSize)),
            _serializationOptions
        );
    }

    [Benchmark(Baseline = true, Description = "UTF-8 bytes -> string -> typed object")]
    public BenchmarkPayload? StringIntermediate()
    {
        var json = JobsHelper.ReadJobRequestAsString(_request, _serializationOptions);
        return JsonSerializer.Deserialize<BenchmarkPayload>(json, _serializationOptions.SerializerOptions);
    }

    [Benchmark(Description = "UTF-8/GZip stream -> typed object")]
    public BenchmarkPayload? DirectTypedRead()
    {
        return JobsHelper.ReadJobRequest<BenchmarkPayload>(_request, _serializationOptions);
    }

    public sealed record BenchmarkPayload(string Value);
}
