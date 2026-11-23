// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

/// <summary>
/// The grants a group of permissions holds for one provider target (for example a role), with the version a later
/// <see cref="IPermissionManager.SetVersionedAsync"/> of the same group must present.
/// </summary>
/// <param name="Grants">
/// One entry per requested permission: <see langword="true"/> when the target is granted it, <see langword="false"/>
/// when it is explicitly prohibited, and <see langword="null"/> when the target holds no record for it.
/// </param>
/// <param name="Version">
/// An opaque token that changes whenever a record of the group is granted, prohibited, or cleared for this target in
/// the current tenant. Use it as an HTTP entity tag.
/// </param>
[PublicAPI]
public sealed record VersionedPermissionGrants(IReadOnlyDictionary<string, bool?> Grants, string Version);
