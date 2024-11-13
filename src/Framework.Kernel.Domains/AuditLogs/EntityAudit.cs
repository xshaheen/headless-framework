// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

#region Entity + Create

public abstract class EntityWithCreateAudit : Entity, ICreateAudit
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class EntityWithCreateAudit<TById, TBy> : Entity, ICreateAudit<TById, TBy>
    where TBy : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TById CreatedById { get; init; }

    public required TBy CreatedBy { get; init; }
}

#endregion

#region Entity + Update

public abstract class EntityWithUpdateAudit : Entity, IUpdateAudit
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class EntityWithUpdateAudit<TById, TBy> : Entity, IUpdateAudit<TById, TBy>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public TById? UpdatedById { get; private set; }

    public TBy? UpdatedBy { get; private set; }

    public void Update(DateTimeOffset now, TById byId, TBy? by = default)
    {
        DateUpdated = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}

#endregion

#region Entity + Suspend

public abstract class EntityWithDeleteAudit : Entity, IDeleteAudit
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithDeleteAudit<TById, TBy> : Entity, IDeleteAudit<TById, TBy>
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TById? DeletedById { get; private set; }

    public TBy? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TById? RestoredById { get; private set; }

    public TBy? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

#endregion

#region Entity + Suspend

public abstract class EntityWithSuspendAudit : Entity, ISuspendAudit
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithSuspendAudit<TById, TBy> : Entity, ISuspendAudit<TById, TBy>
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TById? SuspendedById { get; private set; }

    public TBy? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TById? RestoredById { get; private set; }

    public TBy? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

#endregion

#region Entity + Create + Update

public abstract class EntityWithCreateUpdateAudit : Entity, ICreateAudit, IUpdateAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class EntityWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : Entity,
        ICreateAudit<TCreatorId, TCreator>,
        IUpdateAudit<TEditorId, TEditor>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public TEditorId? UpdatedById { get; private set; }

    public TEditor? UpdatedBy { get; private set; }

    public void Update(DateTimeOffset now, TEditorId byId, TEditor? by = default)
    {
        DateUpdated = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}

public abstract class EntityWithCreateUpdateAudit<TCreatorId, TCreator>
    : EntityWithCreateUpdateAudit<TCreatorId, TCreator, TCreatorId?, TCreator?>
    where TCreator : class?;

#endregion

#region Entity + Create + Delete

public abstract class EntityWithCreateDeleteAudit : Entity, ICreateAudit, IDeleteAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateDeleteAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : Entity,
        ICreateAudit<TCreatorId, TCreator>,
        IDeleteAudit<TEditorId, TEditor>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TEditorId? DeletedById { get; private set; }

    public TEditor? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateDeleteAudit<TAccountId, TAccount>
    : EntityWithCreateDeleteAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region Entity + Create + Suspend

public abstract class EntityWithCreateSuspendAudit : Entity, ICreateAudit, ISuspendAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateSuspendAudit<TCreatorId, TCreator, TSuspendById, TSuspendedBy>
    : Entity,
        ICreateAudit<TCreatorId, TCreator>,
        ISuspendAudit<TSuspendById, TSuspendedBy>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TSuspendById? SuspendedById { get; private set; }

    public TSuspendedBy? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TSuspendById? RestoredById { get; private set; }

    public TSuspendedBy? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TSuspendById? byId = default, TSuspendedBy? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TSuspendById? byId = default, TSuspendedBy? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateSuspendAudit<TAccountId, TAccount>
    : EntityWithCreateSuspendAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region Entity + Create + Update + Delete

public abstract class EntityWithCreateUpdateDeleteAudit : Entity, ICreateAudit, IUpdateAudit, IDeleteAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateUpdateDeleteAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>,
        IDeleteAudit<TEditorId, TEditor>
    where TCreator : class?
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TEditorId? DeletedById { get; private set; }

    public TEditor? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateUpdateDeleteAudit<TAccountId, TAccount>
    : EntityWithCreateUpdateDeleteAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region Entity + Create + Update + Suspend

public abstract class EntityWithCreateUpdateSuspendAudit : Entity, ICreateAudit, IUpdateAudit, ISuspendAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }

    public void Suspended(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateUpdateSuspendAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>,
        ISuspendAudit<TEditorId, TEditor>
    where TCreator : class?
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TEditorId? SuspendedById { get; private set; }

    public TEditor? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateUpdateSuspendAudit<TAccountId, TAccount>
    : EntityWithCreateUpdateSuspendAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region Entity<Id> + Create

public abstract class EntityWithCreateAudit<TId> : Entity<TId>, ICreateAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class EntityWithCreateAudit<TId, TCreatorId, TCreator> : Entity<TId>, ICreateAudit<TCreatorId, TCreator>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }
}

#endregion

#region Entity<Id> + Update

public abstract class EntityWithUpdateAudit<TId> : Entity<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class EntityWithUpdateAudit<TId, TById, TBy> : Entity<TId>, IUpdateAudit<TById, TBy>
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public TById? UpdatedById { get; private set; }

    public TBy? UpdatedBy { get; private set; }

    public void Update(DateTimeOffset now, TById byId, TBy? by = default)
    {
        DateUpdated = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}

#endregion

#region Entity<Id> + Suspend

public abstract class EntityWithDeleteAudit<TId> : Entity<TId>, IDeleteAudit
    where TId : IEquatable<TId>
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithDeleteAudit<TId, TById, TBy> : Entity<TId>, IDeleteAudit<TById, TBy>
    where TId : IEquatable<TId>
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TById? DeletedById { get; private set; }

    public TBy? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TById? RestoredById { get; private set; }

    public TBy? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

#endregion

#region Entity<Id> + Suspend

public abstract class EntityWithSuspendAudit<TId> : Entity<TId>, ISuspendAudit
    where TId : IEquatable<TId>
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithSuspendAudit<TId, TById, TBy> : Entity<TId>, ISuspendAudit<TById, TBy>
    where TId : IEquatable<TId>
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TById? SuspendedById { get; private set; }

    public TBy? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TById? RestoredById { get; private set; }

    public TBy? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TById? byId = default, TBy? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

#endregion

#region Entity<Id> + Create + Update

public abstract class EntityWithCreateUpdateAudit<TId> : EntityWithCreateAudit<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class EntityWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateAudit<TId, TCreatorId, TCreator>,
        IUpdateAudit<TEditorId, TEditor>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public TEditorId? UpdatedById { get; private set; }

    public TEditor? UpdatedBy { get; private set; }

    public void Update(DateTimeOffset now, TEditorId byId, TEditor? by = default)
    {
        DateUpdated = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}

public abstract class EntityWithCreateUpdateAudit<TId, TCreatorId, TCreator>
    : EntityWithCreateUpdateAudit<TId, TCreatorId, TCreator, TCreatorId?, TCreator?>
    where TId : IEquatable<TId>
    where TCreator : class?;

#endregion

#region Entity<Id> + Create + Delete

public abstract class EntityWithCreateDeleteAudit<TId> : Entity<TId>, ICreateAudit, IDeleteAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : Entity<TId>,
        ICreateAudit<TCreatorId, TCreator>,
        IDeleteAudit<TEditorId, TEditor>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TEditorId? DeletedById { get; private set; }

    public TEditor? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateDeleteAudit<TId, TAccountId, TAccount>
    : EntityWithCreateDeleteAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region Entity<Id> + Create + Suspend

public abstract class EntityWithCreateSuspendAudit<TId> : Entity<TId>, ICreateAudit, ISuspendAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateSuspendAudit<TId, TCreatorId, TCreator, TSuspendById, TSuspendedBy>
    : Entity<TId>,
        ICreateAudit<TCreatorId, TCreator>,
        ISuspendAudit<TSuspendById, TSuspendedBy>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TSuspendById? SuspendedById { get; private set; }

    public TSuspendedBy? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TSuspendById? RestoredById { get; private set; }

    public TSuspendedBy? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TSuspendById? byId = default, TSuspendedBy? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TSuspendById? byId = default, TSuspendedBy? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateSuspendAudit<TId, TAccountId, TAccount>
    : EntityWithCreateSuspendAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region Entity<Id> + Create + Update + Delete

public abstract class EntityWithCreateUpdateDeleteAudit<TId> : Entity<TId>, ICreateAudit, IUpdateAudit, IDeleteAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateUpdateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>,
        IDeleteAudit<TEditorId, TEditor>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DateDeleted { get; private set; }

    public TEditorId? DeletedById { get; private set; }

    public TEditor? DeletedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Delete(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsDeleted = true;
        DateDeleted = now;
        DeletedById = byId;
        DeletedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsDeleted)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsDeleted = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateUpdateDeleteAudit<TId, TAccountId, TAccount>
    : EntityWithCreateUpdateDeleteAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region Entity<Id> + Create + Update + Suspend

public abstract class EntityWithCreateUpdateSuspendAudit<TId> : Entity<TId>, ICreateAudit, IUpdateAudit, ISuspendAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }

    public void Suspended(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

public abstract class EntityWithCreateUpdateSuspendAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>,
        ISuspendAudit<TEditorId, TEditor>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public TEditorId? SuspendedById { get; private set; }

    public TEditor? SuspendedBy { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public TEditorId? RestoredById { get; private set; }

    public TEditor? RestoredBy { get; private set; }

    public void Suspend(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been suspended.");
        }

        IsSuspended = true;
        DateSuspended = now;
        SuspendedById = byId;
        SuspendedBy = by;
    }

    public void Restore(DateTimeOffset now, TEditorId? byId = default, TEditor? by = default)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been suspended.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

public abstract class EntityWithCreateUpdateSuspendAudit<TId, TAccountId, TAccount>
    : EntityWithCreateUpdateSuspendAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion
