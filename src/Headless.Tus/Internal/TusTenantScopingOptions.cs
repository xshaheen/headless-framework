// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// Whether uploads are scoped by tenant. It is off unless the application's tenancy setup turns it on; no upload
/// endpoint option reaches it.
/// </summary>
internal sealed class TusTenantScopingOptions
{
    /// <summary>
    /// When <see langword="true"/>, every upload must belong to a tenant, so a request that creates one with no
    /// tenant is refused. An upload is stamped with the request's tenant either way.
    /// </summary>
    public bool Enabled { get; set; }
}
