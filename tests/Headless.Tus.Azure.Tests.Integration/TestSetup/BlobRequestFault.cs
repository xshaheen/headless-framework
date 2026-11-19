// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Tests.TestSetup;

/// <summary>
/// An Azure SDK pipeline policy that fails every request matching a predicate while armed, before it reaches Azure.
/// Added at <see cref="HttpPipelinePosition.PerCall" />, it sits outside the SDK's retry policy, so one failed call
/// fails at once instead of waiting through the SDK's back-off.
/// </summary>
internal sealed class BlobRequestFault : HttpPipelinePolicy
{
    private volatile Func<Request, bool>? _matches;

    /// <summary>Fails each request <paramref name="matches"/> accepts until <see cref="Disarm"/>.</summary>
    public void Arm(Func<Request, bool> matches)
    {
        _matches = matches;
    }

    public void Disarm()
    {
        _matches = null;
    }

    /// <summary>A ranged download of the blob whose name ends with <paramref name="fileId"/>.</summary>
    public static Func<Request, bool> RangedDownload(string fileId)
    {
        return request =>
            request.Method == RequestMethod.Get
            && _IsBlob(request, fileId)
            && !request.Uri.Query.Contains("comp=", StringComparison.Ordinal)
            && (request.Headers.Contains("x-ms-range") || request.Headers.Contains("Range"));
    }

    /// <summary>A block-list commit (Put Block List) of the blob whose name ends with <paramref name="fileId"/>.</summary>
    public static Func<Request, bool> BlockListCommit(string fileId)
    {
        return request =>
            request.Method == RequestMethod.Put
            && _IsBlob(request, fileId)
            && request.Uri.Query.Contains("comp=blocklist", StringComparison.Ordinal);
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        _ThrowIfMatched(message);

        return ProcessNextAsync(message, pipeline);
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        _ThrowIfMatched(message);
        ProcessNext(message, pipeline);
    }

    private void _ThrowIfMatched(HttpMessage message)
    {
        if (_matches?.Invoke(message.Request) == true)
        {
            throw new RequestFailedException(500, "Injected storage failure");
        }
    }

    private static bool _IsBlob(Request request, string fileId)
    {
        return request.Uri.Path.EndsWith("/" + fileId, StringComparison.Ordinal);
    }
}
