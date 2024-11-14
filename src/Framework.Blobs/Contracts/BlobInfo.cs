// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

[DebuggerDisplay("Path = {Path}, Created = {Created}, Modified = {Modified}, Size = {Size} bytes")]
public sealed class BlobInfo
{
    public required string Path { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required DateTimeOffset Modified { get; init; }

    /// <summary>In Bytes</summary>
    public long Size { get; init; }
}
