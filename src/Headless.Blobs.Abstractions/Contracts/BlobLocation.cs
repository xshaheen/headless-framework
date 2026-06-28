// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Identifies a single blob by its top-level <see cref="Container"/> (the provider root — S3 bucket, Azure container,
/// SFTP/file-system root, Redis key prefix) and its container-relative <see cref="Path"/> (the object key, which may
/// contain <c>/</c> separators).
/// </summary>
/// <remarks>
/// The value is validated for path security — traversal sequences, absolute paths, control characters, and any segment
/// ending in the reserved sidecar-metadata suffix — at construction, so every operation that accepts a
/// <see cref="BlobLocation"/> is guarded before it reaches a provider. Provider-specific normalization
/// (bucket/container naming rules, object-key normalization) is applied by the provider when it resolves the location,
/// not by this type — normalization rules differ per backend.
/// </remarks>
[PublicAPI]
public readonly record struct BlobLocation
{
    /// <summary>Creates a location from a container and a container-relative object key.</summary>
    /// <param name="container">The top-level container (bucket/container/root). Must not be null, empty, or whitespace.</param>
    /// <param name="path">The container-relative object key; may contain <c>/</c> separators. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="container"/> or <paramref name="path"/> is empty/whitespace, contains a
    /// path-traversal sequence, is an absolute path, contains control characters, or — for <paramref name="path"/> —
    /// contains a segment that collides with the reserved sidecar-metadata suffix.
    /// </exception>
    public BlobLocation(string container, string path)
    {
        Container = Argument.IsNotNullOrWhiteSpace(container);
        Path = Argument.IsNotNullOrWhiteSpace(path);

        PathValidation.ValidatePathSegment(container);
        PathValidation.ValidatePathSegment(path);

        if (BlobStorageHelpers.HasSidecarSegment(path))
        {
            throw new ArgumentException(
                $"Blob key segments ending in the reserved sidecar suffix '{BlobStorageHelpers.SidecarSuffix}' are not allowed.",
                nameof(path)
            );
        }
    }

    /// <summary>Creates a location from a container and hierarchical path segments joined with <c>/</c>.</summary>
    /// <param name="container">The top-level container (bucket/container/root).</param>
    /// <param name="segments">Path segments joined with <c>/</c> to form the object key.</param>
    public BlobLocation(string container, params ReadOnlySpan<string> segments)
        : this(container, string.Join('/', segments)) { }

    /// <summary>The top-level container (bucket/container/root) that holds the blob.</summary>
    public string Container { get; }

    /// <summary>The container-relative object key; may contain <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>Returns <c>Container:Path</c> for diagnostics.</summary>
    public override string ToString() => $"{Container}:{Path}";
}
