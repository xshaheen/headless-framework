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
/// leniently through <see cref="IBlobNamingNormalizer.NormalizeBlobName"/>. Path security has already been enforced by
/// the <see cref="BlobLocation"/> / <see cref="BlobQuery"/> constructor, so this step only normalizes — it does not
/// re-validate.
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

        return (container, key);
    }

    /// <summary>Resolves a validated <paramref name="query"/> to its backend <c>(container, prefix)</c> pair.</summary>
    /// <param name="query">The validated listing/delete query.</param>
    /// <param name="normalizer">The provider's naming normalizer.</param>
    /// <returns>The backend container name (strict) and normalized prefix, or a <see langword="null"/> prefix when none was supplied.</returns>
    public static (string Container, string? Prefix) ResolveQuery(BlobQuery query, IBlobNamingNormalizer normalizer)
    {
        var container = normalizer.NormalizeContainerName(query.Container);

        var prefix = string.IsNullOrEmpty(query.Prefix) ? null : _NormalizeKey(query.Prefix, normalizer);

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
}
