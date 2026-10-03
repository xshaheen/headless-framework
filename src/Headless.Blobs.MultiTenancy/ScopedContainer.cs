// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internal;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Blobs;

/// <summary>The physical container and key prefix one logical container maps to for the ambient tenant.</summary>
internal readonly record struct ScopedContainer(string PhysicalContainer, string KeyPrefix)
{
    public BlobLocation Apply(BlobLocation location)
    {
        return new BlobLocation(PhysicalContainer, KeyPrefix + location.Path);
    }

    public BlobQuery Apply(BlobQuery query)
    {
        return new BlobQuery(
            PhysicalContainer,
            KeyPrefix + query.Prefix,
            query.PageSize,
            query.ContinuationToken,
            query.IncludeMetadata
        );
    }

    public string ApplyToPath(string path)
    {
        return KeyPrefix + path;
    }

    /// <summary>Removes the key prefix from a key the wrapped store returned.</summary>
    /// <exception cref="InvalidOperationException">The key lies outside the tenant's prefix.</exception>
    public string Strip(string key)
    {
        // Every key a scoped call can reach starts with the prefix; one that does not is another tenant's blob or a
        // host blob, and returning it would leak it across the tenant boundary.
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The blob store returned a key outside the ambient tenant's scope; refusing to return it."
            );
        }

        return key[KeyPrefix.Length..];
    }

    public BlobInfo Strip(BlobInfo info)
    {
        return new BlobInfo
        {
            BlobKey = Strip(info.BlobKey),
            Created = info.Created,
            Modified = info.Modified,
            Size = info.Size,
            Metadata = info.Metadata,
        };
    }

    public BlobBulkResult Strip(BlobBulkResult result, string container)
    {
        var path = Strip(result.Path);

        return result.Location is null
            ? new BlobBulkResult(container, path, result.Result)
            : new BlobBulkResult(new BlobLocation(container, path), result.Result);
    }
}
