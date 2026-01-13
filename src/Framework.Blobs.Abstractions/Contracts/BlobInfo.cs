// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

[DebuggerDisplay("BlobKey = {BlobKey}, Created = {Created}, Modified = {Modified}, Size = {Size} bytes")]
public sealed class BlobInfo
{
    public required string BlobKey { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required DateTimeOffset Modified { get; init; }

    /// <summary>In Bytes</summary>
    public long Size { get; init; }

    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }
}
