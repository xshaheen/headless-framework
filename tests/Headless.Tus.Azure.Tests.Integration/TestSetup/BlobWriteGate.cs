// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Tests.TestSetup;

/// <summary>
/// An Azure SDK pipeline policy that holds back one kind of blob write (a request with the given
/// method and, when <paramref name="comp"/> is set, that <c>?comp=</c> operation) for one blob until
/// the test releases it. Two writers racing on the same upload can then be lined up
/// deterministically: both read the blob state, both arrive at the gated write, and the test
/// decides which write reaches Azure first.
/// </summary>
internal sealed class BlobWriteGate(RequestMethod method, string? comp) : HttpPipelinePolicy, IDisposable
{
    private readonly SemaphoreSlim _release = new(0);
    private readonly Channel<bool> _arrivals = Channel.CreateUnbounded<bool>();
    private volatile string? _armedFileId;

    /// <summary>
    /// Gates the matching writes to the blob whose name ends with <paramref name="fileId"/> until
    /// <see cref="Disarm"/>.
    /// </summary>
    public void Arm(string fileId)
    {
        _armedFileId = fileId;
    }

    public void Disarm()
    {
        _armedFileId = null;
    }

    /// <summary>Completes when one more gated write has arrived and is waiting for release.</summary>
    public ValueTask<bool> WaitForArrivalAsync(CancellationToken cancellationToken)
    {
        return _arrivals.Reader.ReadAsync(cancellationToken);
    }

    /// <summary>Lets exactly one waiting write proceed to Azure.</summary>
    public void ReleaseOne()
    {
        _release.Release();
    }

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        if (_IsGated(message))
        {
            await _arrivals.Writer.WriteAsync(true).ConfigureAwait(false);
            await _release.WaitAsync().ConfigureAwait(false);
        }

        await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        // The store only issues async requests; a synchronous request is never gated.
        ProcessNext(message, pipeline);
    }

    public void Dispose()
    {
        _release.Dispose();
    }

    private bool _IsGated(HttpMessage message)
    {
        var fileId = _armedFileId;
        var request = message.Request;

        return fileId is not null
            && request.Method == method
            && request.Uri.Path.EndsWith("/" + fileId, StringComparison.Ordinal)
            && (
                comp is null
                || request.Uri.Query.TrimStart('?').Split('&').Contains("comp=" + comp, StringComparer.Ordinal)
            );
    }
}
