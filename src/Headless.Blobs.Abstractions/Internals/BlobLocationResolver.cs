// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
        var key = _NormalizeKey(location.Path, normalizer, allowTrailingSlash: false);

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
            prefix = _NormalizeKey(query.Prefix, normalizer, allowTrailingSlash: true);

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

    /// <summary>
    /// Resolves a raw top-level <paramref name="container"/> name to its backend form for container-lifecycle
    /// operations (<see cref="IBlobContainerManager"/>), which address a container without a
    /// <see cref="BlobLocation"/>. Runs the same validate → normalize → re-validate sequence in both directions as
    /// <see cref="Resolve"/>: provider normalizers are lossy, so the normalized result is re-checked to keep a
    /// container from resolving to the storage root or a traversal segment.
    /// </summary>
    /// <param name="container">The raw container name.</param>
    /// <param name="normalizer">The provider's naming normalizer.</param>
    /// <returns>The normalized backend container name.</returns>
    public static string ResolveContainer(string container, IBlobNamingNormalizer normalizer)
    {
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        var normalized = normalizer.NormalizeContainerName(container);

        if (string.IsNullOrWhiteSpace(normalized) || normalized is "." or "..")
        {
            throw new ArgumentException(
                "The blob container resolves to the storage root after provider normalization.",
                nameof(container)
            );
        }

        PathValidation.ValidatePathSegment(normalized);

        return normalized;
    }

    private static string _NormalizeKey(string path, IBlobNamingNormalizer normalizer, bool allowTrailingSlash)
    {
        var segments = path.Split('/');

        for (var i = 0; i < segments.Length; i++)
        {
            var rawSegment = segments[i];
            segments[i] = normalizer.NormalizeBlobName(segments[i]);

            if (string.IsNullOrEmpty(segments[i]))
            {
                var isIntentionalTrailingSlash =
                    allowTrailingSlash && i == segments.Length - 1 && rawSegment.Length == 0;

                if (isIntentionalTrailingSlash)
                {
                    continue;
                }

                throw new ArgumentException(
                    "The blob key contains a segment that is empty after provider normalization.",
                    nameof(path)
                );
            }

            if (segments[i] is "." or "..")
            {
                throw new ArgumentException(
                    "The blob key contains a relative path segment after provider normalization.",
                    nameof(path)
                );
            }
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
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new ArgumentException("The blob container is empty after provider normalization.", nameof(container));
        }

        PathValidation.ValidatePathSegment(container);

        if (container is "." or "..")
        {
            throw new ArgumentException(
                "The blob container is a relative path segment after provider normalization.",
                nameof(container)
            );
        }

        if (string.IsNullOrEmpty(keyOrPrefix))
        {
            return;
        }

        PathValidation.ValidatePathSegment(keyOrPrefix);

        if (BlobStorageHelpers.HasSidecarSegment(keyOrPrefix))
        {
            throw new ArgumentException(
                $"The blob key contains a segment that collides with the reserved sidecar suffix '{BlobStorageHelpers.SidecarSuffix}' "
                    + "after provider normalization.",
                nameof(keyOrPrefix)
            );
        }
    }
}
