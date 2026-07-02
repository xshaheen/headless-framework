// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;
using Headless.Constants;
using Headless.Primitives;

namespace Headless.Blobs.Internals;

/// <summary>
/// Shared helpers for blob storage providers.
/// </summary>
public static class BlobStorageHelpers
{
    public const string UploadDateMetadataKey = "uploadDate";
    public const string ExtensionMetadataKey = "extension";

    /// <summary>
    /// Reserved object-key suffix used by filesystem-like providers (file system, SFTP) to store a blob's metadata in
    /// a companion ("sidecar") file next to its content. Blob keys ending in this suffix are rejected at
    /// <see cref="BlobLocation"/> construction so user blobs can never collide with a sidecar.
    /// </summary>
    public const string SidecarSuffix = ".hlmeta";

    /// <summary>Returns <see langword="true"/> when <paramref name="key"/> is reserved for sidecar metadata (ends with <see cref="SidecarSuffix"/>).</summary>
    public static bool IsSidecarKey(string key) => key.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> when any <c>/</c>-delimited segment is reserved for sidecar metadata.</summary>
    public static bool HasSidecarSegment(string key)
    {
        foreach (var segment in key.Split('/'))
        {
            if (segment.Length > 0 && IsSidecarKey(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a copy of <paramref name="metadata"/> with the framework-internal keys
    /// (<see cref="UploadDateMetadataKey"/>, <see cref="ExtensionMetadataKey"/>) removed, so callers see only the
    /// metadata they supplied. Returns <see langword="null"/> when nothing user-supplied remains. Providers call this
    /// on the metadata they return from <c>GetBlobInfoAsync</c> / <c>OpenReadStreamAsync</c> / listing.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ToUserMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);

        foreach (var pair in metadata)
        {
            if (
                string.Equals(pair.Key, UploadDateMetadataKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Key, ExtensionMetadataKey, StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            result[pair.Key] = pair.Value;
        }

        return result.Count == 0 ? null : result;
    }

    [return: NotNullIfNotNull(nameof(path))]
    public static string? NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    /// <summary>
    /// Compiles a glob <paramref name="pattern"/> (<c>*</c> = any run of characters, <c>?</c> = any single character)
    /// into a predicate that tests whole blob keys. This is the single shared client-side matcher layered over
    /// <see cref="IBlobStorage.ListAsync"/> — providers no longer own private glob regex.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>A predicate returning <see langword="true"/> when a key matches <paramref name="pattern"/>.</returns>
    public static Func<string, bool> CreateGlobMatcher(string pattern)
    {
        var regexText = Regex
            .Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        var regex = new Regex($"^{regexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);

        return key => regex.IsMatch(key);
    }

    /// <summary>
    /// Returns the literal (wildcard-free) head of a glob <paramref name="pattern"/> — the substring up to the first
    /// <c>*</c> or <c>?</c>, or the whole pattern when it contains no wildcard. Usable as a server-pushed prefix to
    /// narrow enumeration before the client-side matcher runs.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>The longest non-wildcard prefix of <paramref name="pattern"/>.</returns>
    public static string GetLiteralPrefix(string pattern)
    {
        var wildcardIndex = pattern.IndexOfAny(['*', '?']);

        return wildcardIndex < 0 ? pattern : pattern[..wildcardIndex];
    }

    /// <summary>Encodes a continuation token that carries the last key returned by a sorted provider page.</summary>
    public static string EncodeContinuationToken(string startAfterKey)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(startAfterKey));
    }

    /// <summary>Decodes a continuation token produced by <see cref="EncodeContinuationToken"/>.</summary>
    /// <exception cref="ArgumentException">The token is not a valid opaque token produced by this provider.</exception>
    public static string? DecodeContinuationToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException e)
        {
            // A continuation token may round-trip through an untrusted boundary (e.g. a web pagination cursor), so a
            // malformed/forged token must fail as a clean, catchable contract error rather than leaking the backend's
            // FormatException unhandled out of ListAsync.
            throw new ArgumentException(
                "The continuation token is not a valid opaque token produced by this provider.",
                nameof(token),
                e
            );
        }
    }

    /// <summary>
    /// Runs a per-item bulk operation over <paramref name="items"/> with bounded parallelism, returning one
    /// <see cref="BlobBulkResult"/> per input indexed to its enumeration position (so <c>results[i]</c> always
    /// describes <c>items[i]</c> even though the parallel bodies start out of order). For each item it builds the
    /// validated <see cref="BlobLocation"/> from <paramref name="container"/> + <paramref name="pathSelector"/>, runs
    /// <paramref name="body"/>, and records <c>Ok(value)</c>; an unaddressable key or any non-cancellation throw fails
    /// that one item (<c>Fail</c>) without aborting the batch. This is the shared orchestration for the providers'
    /// <c>BulkUpload</c>/<c>BulkDelete</c> per-item paths.
    /// </summary>
    public static async ValueTask<IReadOnlyList<BlobBulkResult>> RunBulkAsync<T>(
        string container,
        IReadOnlyCollection<T> items,
        int maxParallelism,
        Func<T, string> pathSelector,
        Func<BlobLocation, T, CancellationToken, ValueTask<bool>> body,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
        {
            return [];
        }

        var list = items as IReadOnlyList<T> ?? [.. items];
        var results = new BlobBulkResult[list.Count];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel
            .ForAsync(
                0,
                list.Count,
                options,
                async (i, ct) =>
                {
                    var item = list[i];
                    var path = pathSelector(item);

                    try
                    {
                        // Build the location (validates) inside the body so an unaddressable key (traversal, reserved
                        // sidecar suffix, etc.) fails that one item only, never the whole batch.
                        var location = new BlobLocation(container, path);
                        var value = await body(location, item, ct).ConfigureAwait(false);
                        results[i] = new BlobBulkResult(location, Result<bool, Exception>.Ok(value));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        results[i] = new BlobBulkResult(container, path, Result<bool, Exception>.Fail(e));
                    }
                }
            )
            .ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Shared control flow for providers whose Move is a non-atomic copy-then-delete (AWS, Azure, file system,
    /// SFTP; Redis moves atomically server-side and does not use this). The caller resolves both endpoints and
    /// short-circuits its provider-specific self-move check first — resolved-identifier equality is
    /// backend-specific (bucket+key, container+key, full path) and is never derived here. The helper owns the
    /// shared semantics: reject an occupied destination, copy, delete the source, and compensate on a faulted
    /// source delete:
    /// <list type="bullet">
    /// <item><description>The source delete returning <see langword="false"/> (source already absent — a concurrent
    /// delete raced the move) is a completed move: the destination holds the data, so it is kept and the move
    /// reports success.</description></item>
    /// <item><description>The source delete throwing triggers a rollback guarded by re-checking the source: the
    /// destination copy is rolled back only when the source is confirmed intact; when the source is confirmed gone
    /// the destination is the sole surviving copy and is kept; when the re-check itself fails, data-safety bias
    /// skips the rollback (worst case two copies survive, never zero) and the delete exception propagates.
    /// </description></item>
    /// </list>
    /// </summary>
    /// <param name="destinationExistsAsync">Checks whether the destination blob already exists.</param>
    /// <param name="copyAsync">Copies source to destination; <see langword="false"/> when the source is missing.</param>
    /// <param name="deleteSourceAsync">Deletes the source blob; <see langword="false"/> when it was already absent.</param>
    /// <param name="sourceExistsAsync">Re-checks the source blob after a faulted delete. Invoked with
    /// <see cref="CancellationToken.None"/> so compensation still runs when the move itself was cancelled.</param>
    /// <param name="rollbackDestinationAsync">Best-effort delete of the destination copy, receiving the
    /// source-delete exception; it must swallow (and log) its own failure.</param>
    /// <param name="logDestinationKeptSourceGone">Logs that a faulted source delete left the source gone and the
    /// destination copy was kept (source residue such as a metadata sidecar may remain).</param>
    /// <param name="logSourceCheckFailed">Logs that the source re-check failed and the rollback was skipped.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the blob now lives at the destination and is gone from the source.</returns>
    public static async ValueTask<bool> MoveViaCopyThenDeleteAsync(
        Func<CancellationToken, ValueTask<bool>> destinationExistsAsync,
        Func<CancellationToken, ValueTask<bool>> copyAsync,
        Func<CancellationToken, ValueTask<bool>> deleteSourceAsync,
        Func<CancellationToken, ValueTask<bool>> sourceExistsAsync,
        Func<Exception, ValueTask> rollbackDestinationAsync,
        Action<Exception> logDestinationKeptSourceGone,
        Action<Exception> logSourceCheckFailed,
        CancellationToken cancellationToken
    )
    {
        if (await destinationExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            // Reject an occupied destination: Move never overwrites. This also guarantees the compensating delete
            // below can only ever remove the copy this Move just created, never prior destination content.
            return false;
        }

        if (!await copyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            // The delete result is intentionally not acted on: false means the source was already absent (a
            // concurrent delete raced the move), and the destination already holds the data — the move is complete.
            // Rolling back here would destroy the only remaining copy.
            _ = await deleteSourceAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception deleteException)
        {
            bool? sourceIntact = null;

            try
            {
                // CancellationToken.None: compensation must still run when the move itself was cancelled.
                sourceIntact = await sourceExistsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception checkException)
            {
                logSourceCheckFailed(checkException);
            }

            if (sourceIntact is false)
            {
                // The delete faulted after the source blob was already removed (e.g. on sidecar residue): the
                // destination is the sole surviving copy — keep it; rolling back would leave zero copies.
                logDestinationKeptSourceGone(deleteException);

                return true;
            }

            if (sourceIntact is true)
            {
                // Roll back only on a confirmed-intact source: the original remains authoritative, so deleting the
                // copy this Move just created cannot lose data.
                await rollbackDestinationAsync(deleteException).ConfigureAwait(false);
            }

            // sourceIntact is null (the re-check failed): data-safety bias — skip the rollback so the worst case is
            // two surviving copies, never zero.
            throw;
        }
    }

    /// <summary>Counts successful deletes and throws when a bulk delete returned per-entry failures.</summary>
    public static int CountDeletedOrThrow(IReadOnlyCollection<BlobBulkResult> results, string operation)
    {
        var failures = results
            .Where(static result => result.Result.IsFailure)
            .Select(static result => result.Result.Error)
            .ToList();

        if (failures.Count > 0)
        {
            throw new AggregateException($"{operation} failed for {failures.Count} blob(s).", failures);
        }

        return results.Count(static result => result.Result is { IsSuccess: true, Value: true });
    }
}
