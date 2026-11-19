// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>
/// An upload as the store returns it, with the server's stamp left out of its metadata, so whoever reads the file
/// sees what the client sent and nothing else.
/// </summary>
internal sealed class TusStampedFile(ITusFile inner) : ITusFile
{
    public string Id => inner.Id;

    public Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        return inner.GetContentAsync(cancellationToken);
    }

    public async Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        var metadata = await inner.GetMetadataAsync(cancellationToken).ConfigureAwait(false);

        // A copy: the store may hand out a dictionary it keeps.
        var clientMetadata = new Dictionary<string, Metadata>(metadata.Count, metadata.Comparer);

        foreach (var (key, value) in metadata)
        {
            if (!TusUploadStamp.IsReserved(key))
            {
                clientMetadata.Add(key, value);
            }
        }

        return clientMetadata;
    }
}
