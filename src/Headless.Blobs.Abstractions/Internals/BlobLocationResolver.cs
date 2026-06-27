// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs.Internals;

/// <summary>
/// The single seam every provider uses to turn a validated <see cref="BlobLocation"/> / <see cref="BlobQuery"/> into a
/// backend address (container + key/prefix). Routing every operational method through this helper is what makes the
/// path-handling bug class (un-normalized buckets, traversal-via-raw-key) structurally impossible.
/// </summary>
/// <remarks>
/// The two-tier naming model is applied here: the top-level container is normalized strictly through
/// <see cref="IBlobNamingNormalizer.NormalizeContainerName"/>, while each <c>/</c>-delimited key segment is normalized
/// leniently through <see cref="IBlobNamingNormalizer.NormalizeBlobName"/>. Construction-time validation alone is not
/// sufficient: provider normalizers are lossy (they strip characters such as <c>*</c>, <c>:</c>, <c>|</c>), so an input
/// that passed <see cref="BlobLocation"/> / <see cref="BlobQuery"/> validation can normalize <em>into</em> a dangerous
/// form (<c>.*.</c> → <c>..</c>, <c>x.hlmet:a</c> → <c>x.hlmeta</c>, or a non-empty prefix → empty). Path security is
/// therefore re-validated on the normalized result, in this one seam, so the guarantee holds for every provider.
/// </remarks>
public static class BlobLocationResolver
{
    /// <summary>Resolves a validated <paramref name="location"/> to its backend <c>(container, key)</c> pair.</summary>
    /// <param name="location">The validated blob location.</param>
    /// <param name="normalizer">The provider's naming normalizer.</param>
    /// <returns>The backend container name (strict) and object key (lenient, per segment).</returns>
    public static (string Container, string Key) Resolve(BlobLocation location, IBlobNamingNormalizer normalizer)
    {
        var container = normalizer.NormalizeContainerName(location.Container);
        var key = _NormalizeKey(location.Path, normalizer);

        _ValidateResolved(container, key);

        return (container, key);
    }

    /// <summary>Resolves a validated <paramref name="query"/> to its backend <c>(container, prefix)</c> pair.</summary>
    /// <param name="query">The validated listing/delete query.</param>
    /// <param name="normalizer">The provider's naming normalizer.</param>
    /// <returns>The backend container name (strict) and normalized prefix, or a <see langword="null"/> prefix when none was supplied.</returns>
    public static (string Container, string? Prefix) ResolveQuery(BlobQuery query, IBlobNamingNormalizer normalizer)
    {
        var container = normalizer.NormalizeContainerName(query.Container);

        string? prefix = null;

        if (!string.IsNullOrEmpty(query.Prefix))
        {
            prefix = _NormalizeKey(query.Prefix, normalizer);

            // A non-empty prefix that the (lossy) normalizer reduces to empty must NOT silently widen a scoped
            // listing/delete into a whole-container match. Fail closed.
            if (string.IsNullOrEmpty(prefix))
            {
                throw new ArgumentException(
                    "The listing/delete prefix is empty after provider normalization; refusing to treat it as a "
                        + "whole-container match.",
                    nameof(query)
                );
            }
        }

        _ValidateResolved(container, prefix);

        return (container, prefix);
    }

    private static string _NormalizeKey(string path, IBlobNamingNormalizer normalizer)
    {
        var segments = path.Split('/');

        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = normalizer.NormalizeBlobName(segments[i]);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Re-applies path-security validation to the <em>normalized</em> container and key/prefix. Provider normalizers
    /// are lossy, so an input that passed construction-time validation can normalize into a traversal sequence
    /// (<c>.*.</c> → <c>..</c>) or the reserved sidecar suffix (<c>x.hlmet:a</c> → <c>x.hlmeta</c>). Validating here —
    /// in the single seam every provider routes through — keeps the path-handling guarantee true regardless of the
    /// normalizer.
    /// </summary>
    private static void _ValidateResolved(string container, string? keyOrPrefix)
    {
        PathValidation.ValidatePathSegment(container);

        if (string.IsNullOrEmpty(keyOrPrefix))
        {
            return;
        }

        PathValidation.ValidatePathSegment(keyOrPrefix);

        if (BlobStorageHelpers.IsSidecarKey(keyOrPrefix))
        {
            throw new ArgumentException(
                $"The blob key collides with the reserved sidecar suffix '{BlobStorageHelpers.SidecarSuffix}' after "
                    + "provider normalization.",
                nameof(keyOrPrefix)
            );
        }
    }
}
