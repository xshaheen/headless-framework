// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>Describes a single blob to be uploaded as part of a <see cref="IBlobStorage.BulkUploadAsync"/> batch.</summary>
/// <param name="Stream">The content to upload.</param>
/// <param name="FileName">The target blob name within the container.</param>
/// <param name="Metadata">Optional key/value metadata. Providers that do not support metadata silently ignore this.</param>
public sealed record BlobUploadRequest(Stream Stream, string FileName, Dictionary<string, string?>? Metadata = null);
