// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs.Redis;

/// <summary>Builds the two Redis hash keys that back one blob container.</summary>
internal static class RedisBlobKeys
{
    /// <summary>
    /// Returns the content hash <c>{container}/</c> and the info hash <c>blob-info/{container}/</c>.
    /// </summary>
    /// <remarks>
    /// Both keys carry the container as a Redis hash tag, so Redis Cluster places them in the same slot and the
    /// upload, delete, and same-container move/copy scripts can touch both in one call. Without the shared tag every
    /// two-key script fails with CROSSSLOT on a cluster.
    /// </remarks>
    /// <exception cref="ArgumentException">The container name contains <c>{</c> or <c>}</c>.</exception>
    public static (string BlobsHash, string InfoHash) Build(string normalizedContainer)
    {
        // A brace inside the name would end the hash tag early, or leave it empty so Redis hashes the whole key and
        // the two keys land in different slots again. The tag belongs to the storage, so the name may not carry one.
        if (normalizedContainer.AsSpan().IndexOfAny('{', '}') >= 0)
        {
            throw new ArgumentException(
                $"Redis blob container names cannot contain '{{' or '}}' because the storage uses the container as a Redis hash tag: '{normalizedContainer}'.",
                nameof(normalizedContainer)
            );
        }

        var tag = "{" + normalizedContainer + "}/";

        return (tag, "blob-info/" + tag);
    }
}
