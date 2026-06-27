// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Outcome of one entry in a bulk operation, paired with the raw blob identity it refers to so results are correlated by
/// identity rather than by position.
/// </summary>
/// <remarks>
/// For <see cref="IBlobStorage.BulkUploadAsync"/>: <c>Ok(true)</c> on success, or <c>Fail(ex)</c> on failure. For
/// <see cref="IBlobStorage.BulkDeleteAsync"/>: <c>Ok(true)</c> when the blob was deleted, <c>Ok(false)</c> when it was
/// not found, or <c>Fail(ex)</c> on failure. A per-entry failure does not abort the rest of the batch. Invalid per-entry
/// paths still return their raw <see cref="Container"/> and <see cref="Path"/>; <see cref="Location"/> is populated only
/// when the input successfully formed a validated <see cref="BlobLocation"/>.
/// </remarks>
[PublicAPI]
public sealed record BlobBulkResult
{
    /// <summary>Creates a result for an input that successfully formed a validated <see cref="BlobLocation"/>.</summary>
    /// <param name="location">The validated blob location this result refers to.</param>
    /// <param name="result">The per-entry outcome.</param>
    public BlobBulkResult(BlobLocation location, Result<bool, Exception> result)
        : this(location.Container, location.Path, location, result) { }

    /// <summary>Creates a result for an input that could not form a validated <see cref="BlobLocation"/>.</summary>
    /// <param name="container">The raw input container.</param>
    /// <param name="path">The raw input path.</param>
    /// <param name="result">The per-entry outcome.</param>
    public BlobBulkResult(string container, string path, Result<bool, Exception> result)
        : this(container, path, location: null, result) { }

    private BlobBulkResult(string container, string path, BlobLocation? location, Result<bool, Exception> result)
    {
        Container = Argument.IsNotNull(container);
        Path = Argument.IsNotNull(path);
        Location = location;
        Result = result;
    }

    /// <summary>The top-level container input this result refers to.</summary>
    public string Container { get; }

    /// <summary>The container-relative path input this result refers to.</summary>
    public string Path { get; }

    /// <summary>
    /// The validated blob location, or <see langword="null"/> when the input failed before a
    /// <see cref="BlobLocation"/> could be constructed.
    /// </summary>
    public BlobLocation? Location { get; }

    /// <summary>The per-entry outcome.</summary>
    public Result<bool, Exception> Result { get; }
}
