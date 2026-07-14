// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.DataProtection;

/// <summary>
/// How the <c>DataProtection</c> key container is expected to come into existence when persisting data-protection
/// keys to blob storage. A dedicated enum (rather than a <see langword="bool"/> flag) so the acknowledgment can never
/// collide with the <c>object serviceKey</c> overload of <c>PersistKeysToBlobStorage</c> during overload resolution.
/// </summary>
[PublicAPI]
public enum BlobContainerProvisioning
{
    /// <summary>
    /// Default: an <see cref="Headless.Blobs.IBlobContainerManager"/> must be available to ensure the container when
    /// the storage requires provisioning (<see cref="Headless.Blobs.IBlobStorage.RequiresContainerProvisioning"/>);
    /// otherwise configuration throws.
    /// </summary>
    Managed = 0,

    /// <summary>
    /// The consumer acknowledges the container is provisioned out-of-band (portal, CLI, IaC), suppressing the
    /// guardrail. Required for providers that ship no container manager, such as Cloudflare R2.
    /// </summary>
    PreProvisioned = 1,
}
