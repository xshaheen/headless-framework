// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs;

/// <summary>
/// A blob store that rewrites every location into a scope before it reaches the backend, such as the tenant-scoped
/// store <c>Headless.Blobs.MultiTenancy</c> registers.
/// </summary>
/// <remarks>
/// Code that must address physical locations, because the location it holds is already physical or belongs to no
/// scope, unwraps the store explicitly: <c>storage is IScopedBlobStorage scoped ? scoped.Unscoped : storage</c>.
/// The signed-URL endpoint and the data-protection key ring do this. Application code rarely should: blobs shared
/// by every scope belong in a store that is not scoped.
/// </remarks>
[PublicAPI]
public interface IScopedBlobStorage : IBlobStorage
{
    /// <summary>Gets the wrapped store, which receives locations unchanged.</summary>
    IBlobStorage Unscoped { get; }
}
