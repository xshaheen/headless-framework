// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Domains;

#region Entity

public abstract class EntityWithCreateAudit : Entity, ICreateAudit
{
    public required DateTimeOffset DateCreated { get; init; }
}

public abstract class EntityWithCreateAudit<TCreatorId, TCreator> : Entity, ICreateAudit<TCreatorId, TCreator>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }
}

public abstract class EntityWithCreateUpdateAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateAudit<TCreatorId, TCreator>,
        IUpdateAudit<TEditorId, TEditor>
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

public abstract class EntityWithCreateDeleteAudit<TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateAudit<TCreatorId, TCreator>,
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

#endregion

#region Entity<Id>

public abstract class EntityWithCreateAudit<TId, TCreatorId, TCreator> : Entity<TId>, ICreateAudit<TCreatorId, TCreator>
    where TId : IEquatable<TId>
    where TCreator : class?
{
    public required DateTimeOffset DateCreated { get; init; }

    public required TCreatorId CreatedById { get; init; }

    public required TCreator CreatedBy { get; init; }
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

public abstract class EntityWithCreateDeleteAudit<TId, TCreatorId, TCreator, TEditorId, TEditor>
    : EntityWithCreateAudit<TId, TCreatorId, TCreator>,
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

#endregion
