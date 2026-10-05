// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines last-update audit fields for an entity.</summary>
[PublicAPI]
public interface IUpdateAudit
{
    /// <summary>Gets the timestamp when this entity was last updated.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    DateTimeOffset? UpdatedAt { get; }
}

/// <summary>Defines last-update audit fields that include the modifying account identifier.</summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
[PublicAPI]
public interface IUpdateAudit<out TAccountId> : IUpdateAudit
{
    /// <summary>Gets the identifier of the account that last updated this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccountId? UpdatedById { get; }
}

/// <summary>
/// Defines last-update audit fields that include a navigation reference to the modifying account, and a method to
/// record an update explicitly.
/// </summary>
/// <typeparam name="TAccountId">The account identifier type.</typeparam>
/// <typeparam name="TAccount">The account entity type.</typeparam>
[PublicAPI]
public interface IUpdateAudit<TAccountId, TAccount> : IUpdateAudit<TAccountId>
{
    /// <summary>Gets the navigation reference to the account that last updated this entity.</summary>
    /// <remarks>Populated automatically by persistence infrastructure.</remarks>
    TAccount? UpdatedBy { get; }

    /// <summary>Records the update timestamp and the responsible account.</summary>
    /// <remarks>
    /// Persistence infrastructure stamps the update audit on every save, so call this only when the recorded time or
    /// account must differ from the ambient clock and current user, such as an anonymous flow acting for a known
    /// account. A non-null <paramref name="byId"/> is kept; a <see langword="null"/> one is filled from the current
    /// user, and stays <see langword="null"/> when there is none.
    /// </remarks>
    /// <param name="now">The UTC timestamp of the update.</param>
    /// <param name="byId">The identifier of the account performing the update, or <see langword="null"/> if unknown.</param>
    /// <param name="by">The navigation reference to the account performing the update, or <see langword="null"/> if not loaded.</param>
    void Update(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
