// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

public interface ISuspendAudit
{
    /// <summary>Indicates whether this entity is suspended.</summary>
    bool IsSuspended { get; }

    /// <summary>Date and time the entity was suspended. when it has a value it means that this entity is suspended.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateSuspended { get; }

    /// <summary>Date and time the entity was restored.</summary>
    /// <remarks>(auto)</remarks>
    DateTimeOffset? DateRestored { get; }
}

public interface ISuspendAudit<out TAccountId> : ISuspendAudit
{
    /// <summary>ID of the account that suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? SuspendedById { get; }

    /// <summary>ID of the account that restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccountId? RestoredById { get; }
}

public interface ISuspendAudit<TAccountId, TAccount> : ISuspendAudit<TAccountId>
{
    /// <summary>Expandable link to the account who suspend this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? SuspendedBy { get; }

    /// <summary>Expandable link to the account who restore this entity.</summary>
    /// <remarks>(auto)</remarks>
    TAccount? RestoredBy { get; }

    void Suspend(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);

    void Restore(DateTimeOffset now, TAccountId? byId = default, TAccount? by = default);
}
