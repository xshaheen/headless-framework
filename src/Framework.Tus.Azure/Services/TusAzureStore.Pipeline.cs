// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Azure.Storage.Blobs.Specialized;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusPipelineStore
{
    public Task<long> AppendDataAsync(string fileId, PipeReader pipeReader, CancellationToken cancellationToken)
    {
        // TODO: Optimize to avoid wrapping PipeReader to Stream
        return AppendDataAsync(fileId, pipeReader.AsStream(), cancellationToken);
    }
}
