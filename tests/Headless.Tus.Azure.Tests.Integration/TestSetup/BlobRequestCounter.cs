// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Tests.TestSetup;

/// <summary>
/// An Azure SDK pipeline policy that counts every request by blob operation, so a test can prove how many storage
/// round trips one tus request costs.
/// </summary>
internal sealed class BlobRequestCounter : HttpPipelinePolicy
{
    public const string GetProperties = "GetProperties";
    public const string GetBlockList = "GetBlockList";
    public const string StageBlock = "StageBlock";
    public const string CommitBlockList = "CommitBlockList";
    public const string SetMetadata = "SetMetadata";
    public const string Upload = "Upload";

    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private int _total;

    /// <summary>The number of requests sent since the last <see cref="Reset" />.</summary>
    public int TotalRequests => Volatile.Read(ref _total);

    /// <summary>A snapshot of the per-operation counts, for assertions and failure messages.</summary>
    public IReadOnlyDictionary<string, int> Counts => new Dictionary<string, int>(_counts, StringComparer.Ordinal);

    public int Count(string operation)
    {
        return _counts.GetValueOrDefault(operation);
    }

    public void Reset()
    {
        _counts.Clear();
        Interlocked.Exchange(ref _total, 0);
    }

    public string Describe()
    {
        return string.Join(
            ", ",
            Counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")
        );
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        _Record(message.Request);

        return ProcessNextAsync(message, pipeline);
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        _Record(message.Request);
        ProcessNext(message, pipeline);
    }

    private void _Record(Request request)
    {
        var comp = request
            .Uri.Query.TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && string.Equals(pair[0], "comp", StringComparison.Ordinal))
            .Select(pair => pair[1])
            .FirstOrDefault();

        var method = request.Method.Method;

        var operation = (method, comp) switch
        {
            ("HEAD", null) => GetProperties,
            ("GET", "blocklist") => GetBlockList,
            ("PUT", "block") => StageBlock,
            ("PUT", "blocklist") => CommitBlockList,
            ("PUT", "metadata") => SetMetadata,
            ("PUT", null) => Upload,
            _ => comp is null ? method : $"{method} {comp}",
        };

        _counts.AddOrUpdate(operation, 1, static (_, count) => count + 1);
        Interlocked.Increment(ref _total);
    }
}
