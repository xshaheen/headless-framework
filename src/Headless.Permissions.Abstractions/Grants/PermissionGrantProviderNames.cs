// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Grants;

/// <summary>
/// Canonical names of the built-in grant providers. Use these constants as the <c>providerName</c> argument
/// to <see cref="IPermissionManager"/> instead of hard-coding the literal strings.
/// </summary>
[PublicAPI]
public static class PermissionGrantProviderNames
{
    /// <summary>Grants attached to a role; the <c>providerKey</c> is the role name.</summary>
    public const string Role = "Role";

    /// <summary>Grants attached directly to a user; the <c>providerKey</c> is the user id.</summary>
    public const string User = "User";
}
