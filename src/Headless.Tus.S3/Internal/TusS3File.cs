// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Parsers;

namespace Headless.Tus.Internal;

/// <summary>An upload as tusdotnet's readable store hands it to the application.</summary>
internal sealed class TusS3File(
    string fileId,
    string? metadata,
    bool isComplete,
    IAmazonS3 s3,
    TusS3StoreOptions options,
    TusS3Keys keys,
    ILogger logger
) : ITusFile
{
    public string Id => fileId;

    /// <summary>Streams the finished object from storage; the caller disposes the stream.</summary>
    /// <exception cref="InvalidOperationException">The upload is not complete yet.</exception>
    public async Task<Stream> GetContentAsync(CancellationToken cancellationToken)
    {
        if (!isComplete)
        {
            // An incomplete upload's bytes sit in multipart parts, and S3 serves a part only after the upload completes.
            throw new InvalidOperationException(
                $"Upload {fileId} is not complete; its content is readable once the upload completes."
            );
        }

        try
        {
            var response = await s3.GetObjectAsync(
                    new GetObjectRequest { BucketName = options.Bucket, Key = keys.FinishedObject(fileId) },
                    cancellationToken
                )
                .ConfigureAwait(false);

            return response.ResponseStream;
        }
        catch (Exception e)
        {
            logger.LogS3FailedToGetContent(e, fileId);

            throw;
        }
    }

    public Task<Dictionary<string, Metadata>> GetMetadataAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ParseMetadata(metadata));
    }

    /// <summary>Parses the client's Upload-Metadata value as the store kept it.</summary>
    /// <exception cref="TusStoreException">The stored value is not valid tus metadata.</exception>
    public static Dictionary<string, Metadata> ParseMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
        {
            return new Dictionary<string, Metadata>(StringComparer.Ordinal);
        }

        var parseResult = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, metadata);

        // Creation validated the value, so a parse failure means the stored object was changed out of band; failing
        // loudly beats handing the application a silently truncated set.
        return parseResult.Success
            ? parseResult.Metadata
            : throw new TusStoreException($"Stored TUS metadata is corrupted: {parseResult.ErrorMessage}");
    }

    /// <summary>The metadata as decoded key/value pairs, keys as the client sent them and values read as UTF-8.</summary>
    /// <exception cref="TusStoreException">The stored value is not valid tus metadata.</exception>
    public static Dictionary<string, string> DecodeMetadata(string? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in ParseMetadata(metadata))
        {
            result[key] = value.GetString(Encoding.UTF8);
        }

        return result;
    }
}
