// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs;

/// <summary>Where tenant-scoped blob storage puts the ambient tenant.</summary>
[PublicAPI]
public enum TenantBlobScopingStrategy
{
    /// <summary>
    /// The tenant id becomes the first key segment inside the caller's container:
    /// <c>BlobLocation("avatars", "1.png")</c> is stored as <c>avatars:{tenantId}/1.png</c>.
    /// </summary>
    PathPrefix = 0,

    /// <summary>
    /// Every tenant gets one container named <c>{ContainerPrefix}{tenantId}</c>, and the caller's container becomes
    /// the first key segment inside it: <c>BlobLocation("avatars", "1.png")</c> is stored as
    /// <c>{ContainerPrefix}{tenantId}:avatars/1.png</c>.
    /// </summary>
    ContainerPerTenant = 1,
}
