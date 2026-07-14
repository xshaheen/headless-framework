// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Result of a blob download operation, pairing the content stream with the blob's file name and optional metadata.
/// </summary>
/// <remarks>
/// The caller owns this result and must dispose it after use. Depending on the storage provider, the stream may
/// hold an open network connection or an SFTP pool slot until disposal. Always wrap in <c>await using</c> or
/// dispose in a <see langword="finally"/> block.
/// </remarks>
/// <param name="Stream">Readable stream delivering the blob's content.</param>
/// <param name="FileName">The blob's file name as stored by the provider.</param>
/// <param name="Metadata">Provider-supplied metadata key/value pairs, or <see langword="null"/> when the provider does not return metadata.</param>
[PublicAPI]
public sealed record BlobDownloadResult(
    Stream Stream,
    string FileName,
    IReadOnlyDictionary<string, string>? Metadata = null
) : IAsyncDisposable, IDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stream.Dispose();
    }
}
