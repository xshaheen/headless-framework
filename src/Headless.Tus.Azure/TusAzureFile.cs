// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure;
using Azure.Storage.Blobs.Models;

namespace Headless.Tus.Models;

internal sealed class TusAzureFile
{
    private TusAzureFile() { }

    public required string FileId { get; init; }

    public required string BlobName { get; init; }

    /// <summary>
    /// The committed content length: Azure counts only committed blocks in a block blob's length, so this is the
    /// upload offset. Each block-list commit of this request updates it.
    /// </summary>
    public long CurrentContentLength { get; set; }

    /// <summary>
    /// The offset a client may resume from and the length of the upload's readable content: the committed length,
    /// minus a checksum-trailer chunk that is committed but not yet verified.
    /// </summary>
    public long VerifiedLength => Metadata.GetVerifiedLength(CurrentContentLength);

    /// <summary>
    /// The committed block list, read after the properties this file holds and kept until this request commits a new
    /// list. Reading it after the properties is what keeps the fence sound: a list read later can only be newer than
    /// <see cref="ETag"/>, so a commit built from a list another writer changed fails on the stale ETag instead of
    /// dropping that writer's blocks.
    /// </summary>
    public List<BlobBlock>? CommittedBlocks { get; set; }

    /// <summary>
    /// Bytes this request staged for a checksum-header chunk that waits for verification, so the commit that verifies
    /// it knows the new length without reading the blob again; <see langword="null"/> when the chunk was staged by an
    /// earlier request or nothing waits.
    /// </summary>
    public long? PendingChunkLength { get; set; }

    /// <summary>
    /// The blob ETag this request last observed: read with the file, then replaced by the ETag each
    /// successful write returns. Every write sends it as <c>If-Match</c>, so a request whose view of
    /// the blob is stale (another writer committed in between) is rejected by Azure instead of
    /// overwriting that writer's block list or metadata, while the request's own sequential writes
    /// keep matching.
    /// </summary>
    public required ETag ETag { get; set; }

    public required TusAzureMetadata Metadata { get; init; }

    /// <summary>
    /// The blob's current HTTP headers (content type, cache control, …), captured so that every
    /// <c>CommitBlockList</c> re-supplies them: Azure's Put Block List <em>clears</em> any
    /// <c>x-ms-blob-*</c> property omitted from the request, which would silently wipe headers a
    /// custom <c>ITusAzureBlobHttpHeadersProvider</c> set at creation on the first PATCH.
    /// <c>ContentHash</c> is deliberately not carried over — the content changes with each commit,
    /// so echoing the stored MD5 would persist a stale digest.
    /// </summary>
    public required BlobHttpHeaders HttpHeaders { get; init; }

    /// <summary>The view of a blob this request has just created, empty and carrying the creation's ETag.</summary>
    public static TusAzureFile Created(
        string fileId,
        string blobName,
        ETag etag,
        TusAzureMetadata metadata,
        BlobHttpHeaders httpHeaders
    )
    {
        return new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobName,
            CurrentContentLength = 0,
            CommittedBlocks = [],
            Metadata = metadata,
            ETag = etag,
            // The same fields a properties read captures, so a later commit re-supplies what creation set.
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = httpHeaders.ContentType,
                CacheControl = httpHeaders.CacheControl,
                ContentEncoding = httpHeaders.ContentEncoding,
                ContentLanguage = httpHeaders.ContentLanguage,
                ContentDisposition = httpHeaders.ContentDisposition,
            },
        };
    }

    public static TusAzureFile FromBlobProperties(string fileId, string blobName, BlobProperties properties)
    {
        return new TusAzureFile
        {
            FileId = fileId,
            BlobName = blobName,
            CurrentContentLength = properties.ContentLength,
            Metadata = TusAzureMetadata.FromAzure(properties.Metadata),
            ETag = properties.ETag,
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = properties.ContentType,
                CacheControl = properties.CacheControl,
                ContentEncoding = properties.ContentEncoding,
                ContentLanguage = properties.ContentLanguage,
                ContentDisposition = properties.ContentDisposition,
            },
        };
    }
}
