// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Blobs.Internal;

/// <summary>
/// The single seam every provider uses to turn a validated <see cref="BlobLocation"/> or <see cref="BlobQuery"/> into a
/// backend address (container plus key or prefix). Routing every operational method through this helper prevents
/// path-handling defects such as un-normalized buckets or traversal sequences.
/// </summary>
/// <remarks>
/// The two-tier naming model is applied here: the top-level container is normalized strictly through
/// <see cref="IBlobNamingNormalizer.NormalizeContainerName"/>, while each <c>/</c>-delimited key segment is normalized
/// leniently through <see cref="IBlobNamingNormalizer.NormalizeBlobName"/>. Construction-time validation alone is not
/// sufficient: provider normalizers are lossy (they strip characters such as <c>*</c>, <c>:</c>, <c>|</c>), so an input
/// that passed <see cref="BlobLocation"/> or <see cref="BlobQuery"/> validation can normalize into a dangerous
/// form (<c>.*.</c> to <c>..</c>, <c>x.hlmet:a</c> to <c>x.hlmeta</c>, or a non-empty prefix to empty). Path security is
/// therefore re-validated on the normalized result in this seam so the guarantee holds for every provider.
/// </remarks>
public static class BlobLocationResolver
{
    /// <summary>Resolves a validated <paramref name="location"/> to its backend <c>(container, key)</c> pair.</summary>
    /// <param name="location">The validated blob location.</param>
    /// <param name="normalizer">The provider naming normalizer.</param>
    /// <returns>The backend container name (strict) and object key (lenient, per segment).</returns>
    public static (string Container, string Key) Resolve(BlobLocation location, IBlobNamingNormalizer normalizer)
    {
        var container = normalizer.NormalizeContainerName(location.Container);
        var key = _NormalizeKey(location.Path, normalizer, allowTrailingSlash: false);

        _ValidateResolved(container, key);

        return (container, key);
    }

    /// <summary>Resolves a validated <paramref name="query"/> to its backend <c>(container, prefix)</c> pair.</summary>
    /// <param name="query">The validated listing or delete query.</param>
    /// <param name="normalizer">The provider naming normalizer.</param>
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
    /// Resolves a raw top-level <paramref name="container"/> name to its backend form for container lifecycle
    /// operations (<see cref="IBlobContainerManager"/>), which address a container without a
    /// <see cref="BlobLocation"/>. Runs the same validation and normalization sequence as
    /// <see cref="Resolve"/>.
    /// </summary>
    /// <param name="container">The raw container name.</param>
    /// <param name="normalizer">The provider naming normalizer.</param>
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
        // Every provider operation routes through here and the overwhelming majority of keys are already in
        // normalized form, so the segments are walked in place and the caller gets the original string back when no
        // segment was rewritten. The output buffer is allocated only from the first segment the normalizer actually
        // changes onward; the validation rules below are byte-for-byte the ones a Split/Join pass applied.
        StringBuilder? normalized = null;
        var start = 0;

        while (true)
        {
            var slash = path.IndexOf('/', start);
            var isLastSegment = slash < 0;
            var rawSegment = isLastSegment ? path[start..] : path[start..slash];
            var segment = normalizer.NormalizeBlobName(rawSegment);

            if (string.IsNullOrEmpty(segment))
            {
                var isIntentionalTrailingSlash = allowTrailingSlash && isLastSegment && rawSegment.Length == 0;

                if (!isIntentionalTrailingSlash)
                {
                    throw new ArgumentException(
                        "The blob key contains a segment that is empty after provider normalization.",
                        nameof(path)
                    );
                }
            }
            else if (segment is "." or "..")
            {
                throw new ArgumentException(
                    "The blob key contains a relative path segment after provider normalization.",
                    nameof(path)
                );
            }

            if (normalized is not null)
            {
                normalized.Append('/').Append(segment);
            }
            else if (!string.Equals(segment, rawSegment, StringComparison.Ordinal))
            {
                // First rewritten segment: everything before it is character-identical to the input — including the
                // '/' that terminates the preceding segment — so the untouched head is copied verbatim.
                normalized = new StringBuilder(path.Length).Append(path, 0, start).Append(segment);
            }

            if (isLastSegment)
            {
                return normalized?.ToString() ?? path;
            }

            start = slash + 1;
        }
    }

    /// <summary>
    /// Re-applies path-security validation to the normalized container and key or prefix.
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
