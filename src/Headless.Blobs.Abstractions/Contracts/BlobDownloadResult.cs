// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Result of a blob download operation containing the stream and metadata.
/// </summary>
/// <remarks>
/// <b>IMPORTANT:</b> Dispose this result promptly after use. Holding it open may exhaust
/// connection pools depending on the storage provider.
/// </remarks>
public sealed record BlobDownloadResult(Stream Stream, string FileName, Dictionary<string, string?>? Metadata = null)
    : IAsyncDisposable,
        IDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Stream.Dispose();
    }
}
