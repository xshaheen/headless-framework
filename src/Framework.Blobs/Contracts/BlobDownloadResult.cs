// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed record BlobDownloadResult(Stream Stream, string FileName, IDictionary<string, object?>? Metadata = null);
