// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

#pragma warning disable IL2026, IL3050

namespace Headless.Blobs.Benchmarks;

public class BlobJsonBenchmarks : IAsyncDisposable
{
    private static readonly BlobLocation _Location = new("benchmarks", "payload.json");
    private BenchmarkBlobStorage _storage = null!;

    [Params(256, 4 * 1024, 64 * 1024, 1024 * 1024)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new BenchmarkPayload(new string('x', PayloadSize)));
        _storage = new BenchmarkBlobStorage(bytes);
    }

    [GlobalCleanup]
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _storage.DisposeAsync();
    }

    [Benchmark(Baseline = true, Description = "Blob stream -> string -> reflection")]
    public async ValueTask<BenchmarkPayload?> StringThenReflection()
    {
        var json = await _storage.GetBlobContentAsync(_Location).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BenchmarkPayload>(json!);
    }

    [Benchmark(Description = "Blob stream -> reflection")]
    public ValueTask<BenchmarkPayload?> StreamReflection()
    {
        return _storage.GetBlobContentAsync<BenchmarkPayload>(_Location);
    }

    [Benchmark(Description = "Blob stream -> string -> source generated")]
    public async ValueTask<BenchmarkPayload?> StringThenSourceGenerated()
    {
        var json = await _storage.GetBlobContentAsync(_Location).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json!, BenchmarkJsonContext.Default.BenchmarkPayload);
    }

    [Benchmark(Description = "Blob stream -> source generated")]
    public ValueTask<BenchmarkPayload?> StreamSourceGenerated()
    {
        return _storage.GetBlobContentAsync(_Location, BenchmarkJsonContext.Default.BenchmarkPayload);
    }
}

public sealed record BenchmarkPayload(string Value);

[JsonSerializable(typeof(BenchmarkPayload))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;

internal sealed class BenchmarkBlobStorage(byte[] bytes) : IBlobStorage
{
    public bool RequiresContainerProvisioning => false;

    public ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
#pragma warning disable CA2000 // Ownership is transferred to the BlobDownloadResult disposed by the read helper.
        BlobDownloadResult result = new(new MemoryStream(bytes, writable: false), location.Path);
#pragma warning restore CA2000
        return ValueTask.FromResult<BlobDownloadResult?>(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
