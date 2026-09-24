// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>Describes a single blob to be uploaded as part of a <see cref="IBlobStorage.BulkUploadAsync"/> batch.</summary>
/// <param name="Path">The container-relative object key for the blob.</param>
/// <param name="Stream">The content to upload.</param>
/// <param name="Metadata">Optional key/value metadata to store alongside the blob.</param>
/// <param name="ContentType">
/// The media type to record for the blob, or <see langword="null"/> to derive it from the extension of
/// <paramref name="Path"/>. See <see cref="IBlobStorage.UploadAsync"/>.
/// </param>
[PublicAPI]
public sealed record BlobUploadRequest(
    string Path,
    Stream Stream,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? ContentType = null
);
