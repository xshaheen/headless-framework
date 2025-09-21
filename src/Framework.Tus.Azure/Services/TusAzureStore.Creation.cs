// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Framework.Constants;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Framework.Tus.Services;

public sealed partial class TusAzureStore : ITusCreationStore
{
    public async Task<string> CreateFileAsync(long uploadLength, string? metadata, CancellationToken cancellationToken)
    {
        var fileId = await _fileIdProvider.CreateId(metadata);

        try
        {
            // Metadata
            var blobMetadata = _EncodeMetadata(_DeserializeTusMetadata(metadata));

            _SetCreatedDate(blobMetadata, DateTimeOffset.UtcNow);
            _SetUploadLength(blobMetadata, uploadLength);
            _SetBlockCount(blobMetadata, 0);

            // Don't create the blob yet - it will only appear when committed
            // Just validate that we can create it by checking container access
            if (!_options.HideBlobUntilComplete) // TODO: check this option maybe will cause the metadata to not be created !
            {
                // Create empty blob immediately for visibility
                var blobHttpHeaders = new BlobHttpHeaders { ContentType = ContentTypes.Applications.OctetStream };

                var blockBlobClient = _containerClient.GetBlockBlobClient(_GetBlobName(fileId));

                await blockBlobClient.UploadAsync(
                    Stream.Null,
                    blobHttpHeaders,
                    metadata: blobMetadata,
                    cancellationToken: cancellationToken
                );
            }

            _logger.LogInformation("Created file {FileId} with upload length {UploadLength}", fileId, uploadLength);

            return fileId;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create file with upload length {UploadLength}", uploadLength);

            throw;
        }
    }

    public async Task<string?> GetUploadMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var blobInfo = await _GetBlobInfoAsync(fileId, cancellationToken);

        if (blobInfo == null)
        {
            return null;
        }

        var tusMetadata = _DecodeMetadata(blobInfo.Metadata);

        return _SerializeTusMetadata(tusMetadata);
    }

    private static string _SerializeTusMetadata(Dictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
        {
            return string.Empty;
        }

        var parts = metadata.Select(kvp => $"{kvp.Key} {kvp.Value.ToBase64()}");

        return string.Join(',', parts);
    }

    private Dictionary<string, Metadata> _DeserializeTusMetadata(string? metadata)
    {
        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata);

        if (!parseResult.Success)
        {
            _logger.LogWarning("Invalid metadata format: {Error}", parseResult.ErrorMessage);

            throw new TusStoreException(parseResult.ErrorMessage);
        }

        return parseResult.Metadata;
    }

    private static Dictionary<string, string> _EncodeMetadata(Dictionary<string, Metadata> metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in metadata)
        {
            var newKey = _SanitizeAzureMetadataKey($"{_TusMetadataPrefix}{key}");
            var newValue = value.GetString(Encoding.UTF8).ToBase64();
            result[newKey] = newValue;
        }

        return result;
    }

    private static Dictionary<string, string> _DecodeMetadata(Dictionary<string, string> blobMetadata)
    {
        var tusMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kvp in blobMetadata)
        {
            if (!kvp.Key.StartsWith(_TusMetadataPrefix, StringComparison.Ordinal) || _IsSystemMetadataKey(kvp.Key))
            {
                continue;
            }

            try
            {
                var originalKey = kvp.Key[_TusMetadataPrefix.Length..];
                tusMetadata[originalKey] = kvp.Value.DecodeBase64();
            }
            catch (FormatException)
            {
                // Skip invalid base64 values
            }
        }

        return tusMetadata;
    }

    private static string _SanitizeAzureMetadataKey(string key)
    {
        // Azure metadata keys: must start with letter or underscore, alphanumeric and underscore only
        var sanitized = _AzureMetadataKey().Replace(key, "_").ToLowerInvariant();

        // Ensure it starts with a letter or underscore
        if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_]", RegexOptions.None, 100)]
    private static partial Regex _AzureMetadataKey();
}
