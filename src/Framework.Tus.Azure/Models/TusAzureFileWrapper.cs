// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;

namespace Framework.Tus.Models;

public class TusAzureFileWrapper(TusAzureFile azureFile, BlobClient blobClient, ILogger logger) : ITusFile
{
    public string Id => azureFile.FileId;

    public async Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get content for file {FileId}", Id);
            throw;
        }
    }

    public async Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        return await Task.FromResult(_ConvertToTusMetadata(azureFile.Metadata));
    }

    private static Dictionary<string, Metadata> _ConvertToTusMetadata(Dictionary<string, string> metadata)
    {
        var result = new Dictionary<string, Metadata>(StringComparer.Ordinal);

        foreach (var (key, value) in metadata)
        {
            // Create a metadata instance from the parsed result
            var parsed = Metadata.Parse($"{key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}");

            if (parsed.TryGetValue(key, out var metadataValue))
            {
                result[key] = metadataValue;
            }
        }

        return result;
    }
}
