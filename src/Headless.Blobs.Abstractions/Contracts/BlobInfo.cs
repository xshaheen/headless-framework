// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>Metadata snapshot for a single blob returned by listing or info operations.</summary>
[DebuggerDisplay("BlobKey = {BlobKey}, Created = {Created}, Modified = {Modified}, Size = {Size} bytes")]
public sealed class BlobInfo
{
    /// <summary>Provider-relative key that identifies the blob within its container.</summary>
    public required string BlobKey { get; init; }

    /// <summary>
    /// UTC timestamp when the blob was first uploaded. Providers that do not track creation time (for example
    /// SFTP and the S3 list API) fall back to the last-modified time or <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>UTC timestamp of the most recent write to the blob.</summary>
    public required DateTimeOffset Modified { get; init; }

    /// <summary>Size of the blob content in bytes.</summary>
    public long Size { get; init; }

    /// <summary>
    /// Provider-supplied metadata key/value pairs, or <see langword="null"/> when the provider does not return
    /// metadata (for example SFTP and the S3 list API, which omit per-object metadata from enumeration responses).
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }
}
