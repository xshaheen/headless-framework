// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Outcome of one entry in a bulk operation, paired with the blob it refers to so results are correlated by identity
/// rather than by position.
/// </summary>
/// <remarks>
/// For <see cref="IBlobStorage.BulkUploadAsync"/>: <c>Ok(true)</c> on success, or <c>Fail(ex)</c> on failure. For
/// <see cref="IBlobStorage.BulkDeleteAsync"/>: <c>Ok(true)</c> when the blob was deleted, <c>Ok(false)</c> when it was
/// not found, or <c>Fail(ex)</c> on failure. A per-entry failure does not abort the rest of the batch.
/// </remarks>
/// <param name="Location">The blob this result refers to.</param>
/// <param name="Result">The per-entry outcome.</param>
[PublicAPI]
public sealed record BlobBulkResult(BlobLocation Location, Result<bool, Exception> Result);
