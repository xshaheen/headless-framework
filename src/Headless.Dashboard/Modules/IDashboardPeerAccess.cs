// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A module capability: admits calls from the module's own peers, such as another node forwarding a read or a command
/// to the node that owns the data, on the module's endpoints.
/// </summary>
/// <remarks>
/// <para>
/// The shell accepts <see cref="PeerAuthenticationScheme"/> beside the dashboard's own sign-in on the module's API at
/// <c>{base}/api/{id}</c>, its command endpoints, and its Operation endpoints, in every authentication mode except
/// none. A request that scheme authenticates is authorized by <see cref="PeerPolicy"/> alone: it skips the view,
/// operate, and command permissions a signed-in user needs, because the node that forwarded it checked them for its
/// own user. A peer principal that fails the policy is refused with <c>403</c>. The scheme admits nothing on another
/// module's endpoints or on the shell's own.
/// </para>
/// <para>
/// The module registers the scheme and the policy with the host's authentication and authorization; the shell fails
/// when it maps the module's endpoints if the scheme is not registered, and when it authorizes a peer call if the
/// policy is not. The shell reads both once, when it maps the endpoints.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardPeerAccess
{
    /// <summary>Gets the authentication scheme that signs peer calls in, such as a shared-key or mTLS scheme.</summary>
    string PeerAuthenticationScheme { get; }

    /// <summary>Gets the authorization policy a peer principal must pass.</summary>
    string PeerPolicy { get; }
}
