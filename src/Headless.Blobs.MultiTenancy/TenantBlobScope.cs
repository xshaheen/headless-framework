// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internal;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Blobs;

/// <summary>Maps a logical container to the physical container and key prefix of the ambient tenant.</summary>
internal sealed partial class TenantBlobScope(
    TenantBlobScopingStrategy strategy,
    string containerPrefix,
    ICurrentTenant currentTenant
)
{
    /// <summary>The longest container name S3, R2, and Azure accept; longer names are truncated by their normalizers.</summary>
    public const int MaxContainerNameLength = 63;

    private const int _MinContainerNameLength = 3;

    /// <summary>Resolves the physical container and key prefix for <paramref name="container"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="container"/> is not a valid container.</exception>
    /// <exception cref="MissingTenantContextException">No ambient tenant is set.</exception>
    /// <exception cref="InvalidOperationException">The ambient tenant id cannot be used as a storage segment.</exception>
    public ScopedContainer Resolve(string container)
    {
        // Under ContainerPerTenant the container never reaches a provider as a container, so nothing else would
        // validate it.
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "Tenant-scoped blob storage was used with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId), or keep host-level blobs in a store listed in "
                    + "TenantBlobScopingOptions.UnscopedStores."
            );
        }

        return strategy == TenantBlobScopingStrategy.ContainerPerTenant
            ? new ScopedContainer(_TenantContainer(tenantId), _ContainerSegment(container) + "/")
            : new ScopedContainer(container, _TenantSegment(tenantId) + "/");
    }

    public BlobLocation Apply(BlobLocation location)
    {
        return Resolve(location.Container).Apply(location);
    }

    public BlobQuery Apply(BlobQuery query)
    {
        Argument.IsNotNull(query);

        return Resolve(query.Container).Apply(query);
    }

    private static string _TenantSegment(string tenantId)
    {
        // An allow-list rather than a deny-list: a segment the filesystem-like normalizers would strip or split
        // ('a:b' becomes 'ab', 'a/b' becomes two segments) would land one tenant inside another tenant's prefix.
        // Lowercase only, because a FileSystem store on a case-insensitive disk (macOS, Windows) would put 'Acme'
        // and 'acme' in one directory; folding the case instead would merge the two tenants just the same.
        if (!TenantSegmentRegex.IsMatch(tenantId) || BlobStorageHelpers.HasSidecarSegment(tenantId))
        {
            throw new InvalidOperationException(
                "Tenant-scoped blob storage cannot use this tenant id as a path segment. A tenant id must start and "
                    + "end with a lowercase ASCII letter or digit and contain only lowercase ASCII letters, digits, "
                    + "'.', '_', and '-'."
            );
        }

        return tenantId;
    }

    private string _TenantContainer(string tenantId)
    {
        var name = containerPrefix + tenantId;

        // Provider normalizers lowercase, strip, and truncate container names, so a name outside this shape could
        // normalize to another tenant's container.
        if (name.Length is < _MinContainerNameLength or > MaxContainerNameLength || !TenantContainerRegex.IsMatch(name))
        {
            throw new InvalidOperationException(
                "Tenant-scoped blob storage cannot name a container for this tenant id. The container name "
                    + "(ContainerPrefix + tenant id) must be 3-63 characters of lowercase ASCII letters, digits, and "
                    + "single hyphens, starting and ending with a letter or digit."
            );
        }

        return name;
    }

    private static string _ContainerSegment(string container)
    {
        // The logical container becomes the first key segment, so a separator inside it would let "a/b" + "c" and
        // "a" + "b/c" address the same key.
        if (container.Contains('/', StringComparison.Ordinal) || container.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A container cannot contain '/' or '\\' when blobs are scoped with a container per tenant.",
                nameof(container)
            );
        }

        return container;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TenantSegmentRegex { get; }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TenantContainerRegex { get; }
}
