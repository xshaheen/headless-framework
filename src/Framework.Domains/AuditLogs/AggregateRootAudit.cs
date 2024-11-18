// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Domains;

#region AggregateRoot + Create

public abstract class AggregateRootWithCreateAudit : AggregateRoot, ICreateAudit
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class AggregateRootWithCreateAudit<TById, TBy> : AggregateRoot, ICreateAudit<TById, TBy>
    where TBy : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TById CreatedById { get; init; }

    public required TBy CreatedBy { get; init; }
}

#endregion

#region AggregateRoot + Update

public abstract class AggregateRootWithUpdateAudit : AggregateRoot, IUpdateAudit
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class AggregateRootWithUpdateAudit<TById, TBy> : AggregateRoot, IUpdateAudit<TById, TBy>
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

#region AggregateRoot + Suspend

public abstract class AggregateRootWithDeleteAudit : AggregateRoot, IDeleteAudit
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

public abstract class AggregateRootWithDeleteAudit<TById, TBy> : AggregateRoot, IDeleteAudit<TById, TBy>
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

#region AggregateRoot + Suspend

public abstract class AggregateRootWithSuspendAudit : AggregateRoot, ISuspendAudit
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

public abstract class AggregateRootWithSuspendAudit<TById, TBy> : AggregateRoot, ISuspendAudit<TById, TBy>
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

#region AggregateRoot + Create + Update

public abstract class AggregateRootWithCreateUpdateAudit : AggregateRoot, ICreateAudit, IUpdateAudit
{
    public required DateTimeOffset DateCreated { get; init; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class AggregateRootWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRoot,
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

public abstract class AggregateRootWithCreateUpdateAudit<TCreatorId, TCreator>
    : AggregateRootWithCreateUpdateAudit<TCreatorId, TCreator, TCreatorId?, TCreator?>
    where TCreator : class?;

#endregion

#region AggregateRoot + Create + Delete

public abstract class AggregateRootWithCreateDeleteAudit : AggregateRoot, ICreateAudit, IDeleteAudit
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

public abstract class AggregateRootWithCreateDeleteAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRoot,
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

public abstract class AggregateRootWithCreateDeleteAudit<TAccountId, TAccount>
    : AggregateRootWithCreateDeleteAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region AggregateRoot + Create + Suspend

public abstract class AggregateRootWithCreateSuspendAudit : AggregateRoot, ICreateAudit, ISuspendAudit
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

public abstract class AggregateRootWithCreateSuspendAudit<TCreatorId, TCreator, TSuspendById, TSuspendedBy>
    : AggregateRoot,
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

public abstract class AggregateRootWithCreateSuspendAudit<TAccountId, TAccount>
    : AggregateRootWithCreateSuspendAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region AggregateRoot + Create + Update + Delete

public abstract class AggregateRootWithCreateUpdateDeleteAudit : AggregateRoot, ICreateAudit, IUpdateAudit, IDeleteAudit
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

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>,
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

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TAccountId, TAccount>
    : AggregateRootWithCreateUpdateDeleteAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region AggregateRoot + Create + Update + Suspend

public abstract class AggregateRootWithCreateUpdateSuspendAudit
    : AggregateRoot,
        ICreateAudit,
        IUpdateAudit,
        ISuspendAudit
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

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>,
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

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TAccountId, TAccount>
    : AggregateRootWithCreateUpdateSuspendAudit<TAccountId, TAccount, TAccountId?, TAccount?>
    where TAccount : class?;

#endregion

#region AggregateRoot<Id> + Create

public abstract class AggregateRootWithCreateAudit<TId> : AggregateRoot<TId>, ICreateAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class AggregateRootWithCreateAudit<TId, TCreatorId, TCreator>
    : AggregateRoot<TId>,
        ICreateAudit<TCreatorId, TCreator>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }
}

#endregion

#region AggregateRoot<Id> + Update

public abstract class AggregateRootWithUpdateAudit<TId> : AggregateRoot<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class AggregateRootWithUpdateAudit<TId, TById, TBy> : AggregateRoot<TId>, IUpdateAudit<TById, TBy>
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

#region AggregateRoot<Id> + Suspend

public abstract class AggregateRootWithDeleteAudit<TId> : AggregateRoot<TId>, IDeleteAudit
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

public abstract class AggregateRootWithDeleteAudit<TId, TById, TBy> : AggregateRoot<TId>, IDeleteAudit<TById, TBy>
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

#region AggregateRoot<Id> + Suspend

public abstract class AggregateRootWithSuspendAudit<TId> : AggregateRoot<TId>, ISuspendAudit
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

public abstract class AggregateRootWithSuspendAudit<TId, TById, TBy> : AggregateRoot<TId>, ISuspendAudit<TById, TBy>
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

#region AggregateRoot<Id> + Create + Update

public abstract class AggregateRootWithCreateUpdateAudit<TId> : AggregateRootWithCreateAudit<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public void Update(DateTimeOffset now)
    {
        DateUpdated = now;
    }
}

public abstract class AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateAudit<TId, TCreatorId, TCreator>,
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

public abstract class AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator>
    : AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TCreatorId?, TCreator?>
    where TId : IEquatable<TId>
    where TCreator : class?;

#endregion

#region AggregateRoot<Id> + Create + Delete

public abstract class AggregateRootWithCreateDeleteAudit<TId> : AggregateRoot<TId>, ICreateAudit, IDeleteAudit
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

public abstract class AggregateRootWithCreateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRoot<TId>,
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

public abstract class AggregateRootWithCreateDeleteAudit<TId, TAccountId, TAccount>
    : AggregateRootWithCreateDeleteAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region AggregateRoot<Id> + Create + Suspend

public abstract class AggregateRootWithCreateSuspendAudit<TId> : AggregateRoot<TId>, ICreateAudit, ISuspendAudit
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

public abstract class AggregateRootWithCreateSuspendAudit<TId, TCreatorId, TCreator, TSuspendById, TSuspendedBy>
    : AggregateRoot<TId>,
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

public abstract class AggregateRootWithCreateSuspendAudit<TId, TAccountId, TAccount>
    : AggregateRootWithCreateSuspendAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region AggregateRoot<Id> + Create + Update + Delete

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TId>
    : AggregateRoot<TId>,
        ICreateAudit,
        IUpdateAudit,
        IDeleteAudit
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

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>,
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

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TId, TAccountId, TAccount>
    : AggregateRootWithCreateUpdateDeleteAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion

#region AggregateRoot<Id> + Create + Update + Suspend

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TId>
    : AggregateRoot<TId>,
        ICreateAudit,
        IUpdateAudit,
        ISuspendAudit
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

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>,
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

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TId, TAccountId, TAccount>
    : AggregateRootWithCreateUpdateSuspendAudit<TId, TAccountId, TAccount, TAccountId?, TAccount?>
    where TId : IEquatable<TId>
    where TAccount : class?;

#endregion
