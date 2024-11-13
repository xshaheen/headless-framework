// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed record BlobUploadRequest(Stream Stream, string FileName, Dictionary<string, string?>? Metadata = null);
