// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Tus;
using Microsoft.Extensions.Options;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Demo.Services;

/// <summary>One upload as shown in the demo UI.</summary>
public sealed record UploadSummary(
    string Id,
    string FileName,
    string ContentType,
    long CommittedBytes,
    long? TotalBytes,
    bool IsComplete,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt
);

/// <summary>
/// App-level listing over the TUS container. The tus protocol has no listing operation, so the demo
/// reads the blob container directly and interprets the store's documented metadata keys
/// (<c>tus_*</c>) plus the verbatim <c>tus_metadata</c> Upload-Metadata value.
/// </summary>
public sealed class UploadDirectory(BlobServiceClient blobServiceClient, IOptions<TusAzureStoreOptions> options)
{
    public async Task<List<UploadSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var storeOptions = options.Value;
        var container = blobServiceClient.GetBlobContainerClient(storeOptions.ContainerName);
        var prefix = storeOptions.BlobPrefix.EndsWith('/') ? storeOptions.BlobPrefix : storeOptions.BlobPrefix + "/";
        var uploads = new List<UploadSummary>();

        await foreach (
            var blob in container
                .GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix, cancellationToken)
                .WithCancellation(cancellationToken)
        )
        {
            uploads.Add(_ToSummary(blob, prefix));
        }

        return [.. uploads.OrderByDescending(upload => upload.CreatedAt ?? DateTimeOffset.MinValue)];
    }

    private static UploadSummary _ToSummary(BlobItem blob, string prefix)
    {
        var metadata = blob.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var committed = blob.Properties.ContentLength ?? 0;

        long? total = null;

        if (
            metadata.TryGetValue("tus_upload_length", out var totalText)
            && long.TryParse(totalText, CultureInfo.InvariantCulture, out var parsedTotal)
            && parsedTotal >= 0
        )
        {
            total = parsedTotal;
        }

        var (fileName, contentType) = _ParseUserMetadata(metadata);

        return new UploadSummary(
            Id: blob.Name[prefix.Length..],
            FileName: fileName,
            ContentType: contentType,
            CommittedBytes: committed,
            TotalBytes: total,
            IsComplete: total is not null && committed >= total.Value,
            CreatedAt: _ParseDate(metadata, "tus_created"),
            ExpiresAt: _ParseDate(metadata, "tus_expiration")
        );
    }

    private static (string FileName, string ContentType) _ParseUserMetadata(IDictionary<string, string> metadata)
    {
        // The store keeps the client's Upload-Metadata header verbatim under tus_metadata.
        if (!metadata.TryGetValue("tus_metadata", out var raw) || string.IsNullOrEmpty(raw))
        {
            return ("(unnamed)", "application/octet-stream");
        }

        var parsed = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, raw);

        if (!parsed.Success)
        {
            return ("(unnamed)", "application/octet-stream");
        }

        var fileName = parsed.Metadata.TryGetValue("filename", out var name)
            ? name.GetString(Encoding.UTF8)
            : "(unnamed)";

        var contentType = parsed.Metadata.TryGetValue("filetype", out var type)
            ? type.GetString(Encoding.UTF8)
            : "application/octet-stream";

        return (fileName, contentType);
    }

    private static DateTimeOffset? _ParseDate(IDictionary<string, string> metadata, string key)
    {
        return
            metadata.TryGetValue(key, out var text)
            && DateTimeOffset.TryParseExact(
                text,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var value
            )
            ? value
            : null;
    }
}
