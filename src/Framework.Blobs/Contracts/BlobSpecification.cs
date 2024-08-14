using System.Diagnostics;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

[DebuggerDisplay("Path = {Path}, Created = {Created}, Modified = {Modified}, Size = {Size} bytes")]
public sealed class BlobSpecification
{
    public required string Path { get; init; }

    public required DateTime Created { get; init; }

    public required DateTime Modified { get; init; }

    /// <summary>In Bytes</summary>
    public long Size { get; init; }
}
