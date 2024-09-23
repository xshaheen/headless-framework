// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

#region Date only audit

public abstract class AggregateRootWithCreateAudit<TId> : AggregateRoot<TId>, ICreateAudit
    where TId : IEquatable<TId>
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class AggregateRootWithUpdateAudit<TId> : AggregateRoot<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; set; }
}

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

public abstract class AggregateRootWithCreateUpdateAudit<TId> : AggregateRootWithCreateAudit<TId>, IUpdateAudit
    where TId : IEquatable<TId>
{
    public DateTimeOffset? DateUpdated { get; set; }
}

public abstract class AggregateRootWithCreateUpdateDeleteAudit<TId>
    : AggregateRootWithCreateUpdateAudit<TId>,
        IDeleteAudit
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

public abstract class AggregateRootWithCreateUpdateSuspendAudit<TId>
    : AggregateRootWithCreateUpdateAudit<TId>,
        ISuspendAudit
    where TId : IEquatable<TId>
{
    public bool IsSuspended { get; private set; }

    public DateTimeOffset? DateSuspended { get; private set; }

    public DateTimeOffset? DateRestored { get; private set; }

    public void Suspend(DateTimeOffset now)
    {
        if (IsSuspended)
        {
            throw new InvalidOperationException("The entity has already been deleted.");
        }

        IsSuspended = true;
        DateSuspended = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsSuspended)
        {
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsSuspended = false;
        DateRestored = now;
    }
}

#endregion

#region AggregateRootWithCreateAudit<TId, TCreatorId, TCreator>

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

#region AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>

public abstract class AggregateRootWithCreateUpdateAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : AggregateRootWithCreateAudit<TId, TCreatorId, TCreator>,
        IUpdateAudit<TEditorId, TEditor>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public DateTimeOffset? DateUpdated { get; private set; }

    public TEditorId? UpdatedById { get; private set; }

    public TEditor? UpdatedBy { get; private set; }

    public void Update(DateTimeOffset now, TEditorId? byId, TEditor? by = default)
    {
        DateUpdated = now;
        UpdatedById = byId;
        UpdatedBy = by;
    }
}

#endregion

#region Full: AggregateRootWithCreateUpdateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>

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

#endregion

#region Full AggregateRootWithCreateUpdateSuspendAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>

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
            throw new InvalidOperationException("The entity has already been deleted.");
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
            throw new InvalidOperationException("The entity has not been deleted.");
        }

        IsSuspended = false;
        DateRestored = now;
        RestoredById = byId;
        RestoredBy = by;
    }
}

#endregion
