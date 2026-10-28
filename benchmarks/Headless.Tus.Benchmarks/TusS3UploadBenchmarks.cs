// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;

#pragma warning disable CA1303 // The request summary is console output a person reads, not a localized message.

namespace Headless.Tus.Benchmarks;

/// <summary>
/// Throughput and allocations of one 100 MiB tus upload (create, every PATCH, completion) through the real tusdotnet
/// middleware, for Headless.Tus.S3 and tusdotnet.Stores.S3 against the same MinIO container. Needs Docker. After the
/// last iteration it prints the storage requests one upload cost; <c>-- requests</c> prints them for every case.
/// </summary>
public class TusS3UploadBenchmarks
{
    private readonly List<string> _locations = [];
    private MinioServer _minio = null!;
    private TusUploadHost _host = null!;
    private byte[] _payload = null!;
    private IReadOnlyDictionary<string, int> _lastCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    private int _lastUploads;

    [ParamsSource(nameof(Cases))]
    public UploadCase Case { get; set; } = null!;

    public static IEnumerable<UploadCase> Cases => UploadCase.All.Where(c => c.IsSupported);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _payload = UploadProfiles.CreatePayload();
        _minio = await MinioServer.StartAsync();
        _host = await TusUploadHost.StartAsync(Case.Store, _minio);
    }

    [Benchmark]
    public async Task Upload()
    {
        _locations.Add(await _host.UploadAsync(Case.Profile, _payload));
    }

    // Termination keeps the bucket from growing by 100 MiB an invocation; its requests are not the upload's, so the
    // counts are taken before it and reset after it.
    [IterationCleanup]
    public void Cleanup()
    {
        _lastCounts = _host.Counter.Snapshot();
        _lastUploads = _locations.Count;

#pragma warning disable VSTHRD002, MA0045 // BenchmarkDotNet calls IterationCleanup synchronously and outside the measurement.
        foreach (var location in _locations)
        {
            _host.DeleteAsync(location).GetAwaiter().GetResult();
        }
#pragma warning restore VSTHRD002, MA0045

        _locations.Clear();
        _host.Counter.Reset();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_lastUploads > 0)
        {
            var total = _lastCounts.Values.Sum() / (double)_lastUploads;
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"// Storage requests per upload ({Case}): {total:0.#} [{StorageRequestCounter.Describe(_lastCounts, _lastUploads)}]"
                )
            );
        }

        await _host.DisposeAsync();
        await _minio.DisposeAsync();
    }
}
