// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

[PublicAPI]
public interface ISuspendAudit
{
    /// <summary>Indicates whether this entity is suspended.</summary>
    bool IsSuspended { get; }

    /// <summary>Date and time the entity was suspended. when it has a value it means that this entity is suspended.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateSuspended { get; }

    /// <summary>Date and time the entity was unsuspended.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateUnsuspended { get; }
}

[PublicAPI]
public interface ISuspendAudit<out TAccountId> : ISuspendAudit
{
    /// <summary>ID of the account that suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? SuspendedById { get; }

    /// <summary>ID of the account that unsuspended this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? UnsuspendedById { get; }
}

[PublicAPI]
public interface ISuspendAudit<TAccountId, TAccount> : ISuspendAudit<TAccountId>
{
    /// <summary>Expandable link to the account who suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? SuspendedBy { get; }

    /// <summary>Expandable link to the account who unsuspended this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? UnsuspendedBy { get; }

    void Suspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    void Unsuspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
