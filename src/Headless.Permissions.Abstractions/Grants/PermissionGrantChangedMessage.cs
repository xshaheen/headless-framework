// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Grants;

/// <summary>
/// Message published over <c>IBus</c> by <c>PermissionManager</c> after a write, so peer instances holding a
/// resolved permission grant learn that it is stale. Consumers subscribe with
/// <c>IConsume&lt;PermissionGrantChangedMessage&gt;</c> and re-read the names they care about.
/// </summary>
/// <remarks>
/// <para>
/// This is a "re-read these names" signal, not a value feed. Carrying grant values would make delivery order
/// load-bearing: two racing grant and revoke writes could leave a receiver holding the older state. Re-reading is
/// idempotent and order-free, at the cost of one read per instance per change.
/// </para>
/// <para>
/// The cache is a separate concern and is already handled. A store-backed write evicts its affected cache entries.
/// This message exists for state the framework cannot see: a resolved grant a consumer copied into its own state.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record PermissionGrantChangedMessage
{
    /// <summary>
    /// Names of the permissions whose stored grant changed. Never empty. A delete that clears a whole provider
    /// scope reports every name it removed rather than a wildcard, so a consumer can match on the names it holds
    /// without knowing the provider's contents.
    /// </summary>
    public required IReadOnlyList<string> PermissionNames { get; init; }

    /// <summary>The provider that stored the change, such as <see cref="PermissionGrantProviderNames.Role"/>.</summary>
    public required string ProviderName { get; init; }

    /// <summary>The provider-specific discriminator the change was written under, such as a role or user ID.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>
    /// <c>IHostIdentityAccessor.HostName</c> of the process that wrote the change, for logs and telemetry.
    /// </summary>
    /// <remarks>
    /// Do not filter on it. The writer's own process holds copies too — the manager that stored the grant knows
    /// nothing about the state some consumer copied it into — so the originating instance must re-read like every
    /// other. That is the opposite of a cache invalidation, where the writer already updated its own tier.
    /// </remarks>
    public required string OriginHostName { get; init; }
}
