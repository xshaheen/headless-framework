// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Tus.Models;
using tusdotnet.Interfaces;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusReadableStore
{
    public async Task<ITusFile?> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobClient = _GetBlobClient(fileId);
        var tusFile = await _GetTusFileInfoAsync(blobClient, fileId, cancellationToken);

        return tusFile == null ? null : new TusAzureFileWrapper(tusFile, blobClient, _logger);
    }
}
