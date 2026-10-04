// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Blobs.Internal;
using Headless.Checks;

namespace Headless.Blobs;

/// <summary>
/// Identifies a single blob by its top-level <see cref="Container"/> (the provider root: S3 bucket, Azure container,
/// file-system root, SFTP root, or Redis key prefix) and its container-relative <see cref="Path"/> (the object key, which may
/// contain <c>/</c> separators).
/// </summary>
/// <remarks>
/// The value is validated for path security (traversal sequences, absolute paths, control characters, and any segment
/// ending in the reserved sidecar-metadata suffix) at construction, so every operation that accepts a
/// <see cref="BlobLocation"/> is guarded before it reaches a provider. Provider-specific normalization
/// (bucket or container naming rules, object-key normalization) is applied by the provider when it resolves the location,
/// not by this type. Normalization rules differ per backend.
/// </remarks>
[PublicAPI]
public readonly record struct BlobLocation
{
    /// <summary>Creates a location from a container and a container-relative object key.</summary>
    /// <param name="container">The top-level container (bucket, container, or root). Must not be null, empty, or whitespace.</param>
    /// <param name="path">The container-relative object key; may contain <c>/</c> separators. Must not be null, empty, or whitespace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="container"/> or <paramref name="path"/> is empty or whitespace, contains a
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
    /// <param name="container">The top-level container (bucket, container, or root).</param>
    /// <param name="segments">Path segments joined with <c>/</c> to form the object key.</param>
    public BlobLocation(string container, params ReadOnlySpan<string> segments)
        : this(container: container, path: string.Join('/', segments)) { }

    /// <summary>Creates a location from hierarchical segments where the first segment is the container.</summary>
    /// <param name="segments">
    /// The top-level container (bucket, container, or root) followed by one or more path segments joined with <c>/</c> to
    /// form the object key.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Fewer than two segments are provided, or the container or the resulting path fails validation.
    /// </exception>
    /// <remarks>
    /// Carries <see cref="OverloadResolutionPriorityAttribute"/>, so any all-string argument list binds here first.
    /// The other constructors produce identical values and remain reachable through named arguments or an explicit
    /// <c>(container, span)</c> argument pair. The delegation uses named arguments for the same reason:
    /// a positional <c>this(string, string)</c> initializer would re-resolve to this constructor itself.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public BlobLocation(params ReadOnlySpan<string> segments)
        : this(
            container: segments.Length >= 2
                ? segments[0]
                : throw new ArgumentException(
                    "At least two segments are required: a container followed by one or more path segments.",
                    nameof(segments)
                ),
            path: string.Join('/', segments[1..])
        ) { }

    /// <summary>The top-level container (bucket, container, or root) that holds the blob.</summary>
    public string Container { get; }

    /// <summary>The container-relative object key; may contain <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>Returns <c>Container:Path</c> for diagnostics.</summary>
    public override string ToString()
    {
        return $"{Container}:{Path}";
    }
}
