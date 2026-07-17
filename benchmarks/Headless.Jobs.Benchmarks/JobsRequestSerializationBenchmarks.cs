// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Jobs.Benchmarks;

public class JobsRequestSerializationBenchmarks
{
    private byte[] _request = null!;

    [Params(256, 4 * 1024, 64 * 1024, 1024 * 1024)]
    public int PayloadSize { get; set; }

    [Params(false, true)]
    public bool Compressed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        JobsHelper.RequestJsonSerializerOptions = new JsonSerializerOptions();
        JobsHelper.UseGZipCompression = Compressed;
        _request = JobsHelper.CreateJobRequest(new BenchmarkPayload(new string('x', PayloadSize)));
    }

    [Benchmark(Baseline = true, Description = "UTF-8 bytes -> string -> typed object")]
    public BenchmarkPayload? StringIntermediate()
    {
        JobsHelper.UseGZipCompression = Compressed;
        var json = JobsHelper.ReadJobRequestAsString(_request);
        return JsonSerializer.Deserialize<BenchmarkPayload>(json, JobsHelper.RequestJsonSerializerOptions);
    }

    [Benchmark(Description = "UTF-8/GZip stream -> typed object")]
    public BenchmarkPayload? DirectTypedRead()
    {
        JobsHelper.UseGZipCompression = Compressed;
        return JobsHelper.ReadJobRequest<BenchmarkPayload>(_request);
    }

    public sealed record BenchmarkPayload(string Value);
}
