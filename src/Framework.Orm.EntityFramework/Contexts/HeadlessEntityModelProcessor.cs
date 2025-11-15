// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Framework.Abstractions;
using Framework.Domains;
using Framework.Orm.EntityFramework.Configurations;
using Framework.Primitives;
using Framework.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Contexts;

public interface IHeadlessEntityModelProcessor
{
    string? TenantId { get; }

    void ProcessModelCreating(ModelBuilder modelBuilder);

    ProcessBeforeSaveReport ProcessEntries(DbContext db);
}

public sealed class HeadlessEntityModelProcessor(
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IGuidGenerator guidGenerator,
    IClock clock
) : IHeadlessEntityModelProcessor
{
    public string? TenantId => currentTenant.Id;

    #region Process Model Creating

    public void ProcessModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (!type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                _ConfigureConvention(modelBuilder, type);
            }

            if (type.BaseType is null && !type.IsOwned())
            {
                _ConfigureValueConverter(modelBuilder, type);
            }

            if (type.BaseType is null && !type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                _InvokeConfigureQueryFilters(modelBuilder, type);
            }
        }
    }

    private static void _ConfigureConvention(ModelBuilder builder, IMutableEntityType type)
    {
        builder.Entity(type.ClrType).ConfigureFrameworkConvention();
    }

    private void _ConfigureValueConverter(ModelBuilder builder, IMutableEntityType type)
    {
        var dateTimeType = typeof(DateTime);
        var nullableDateTimeType = typeof(DateTime?);

        var properties = type.GetProperties()
            .Where(property =>
                property.PropertyInfo is { CanWrite: true }
                && (
                    property.PropertyInfo.PropertyType == dateTimeType
                    || property.PropertyInfo.PropertyType == nullableDateTimeType
                )
            )
            .ToList();

        if (properties.Count == 0)
        {
            return;
        }

        var dateTimeConverter = new NormalizeDateTimeValueConverter(clock);
        var nullableDateTimeConverter = new NullableNormalizeDateTimeValueConverter(clock);

        foreach (var property in properties)
        {
            ValueConverter converter = property.ClrType == dateTimeType ? dateTimeConverter : nullableDateTimeConverter;
            builder.Entity(type.ClrType).Property(property.Name).HasConversion(converter);
        }
    }

    private void _InvokeConfigureQueryFilters(ModelBuilder builder, IMutableEntityType type)
    {
        // Note: this is executed once per db context instance
        _ConfigureQueryFiltersMethod.MakeGenericMethod(type.ClrType).Invoke(this, [builder, type]);
    }

    // Note: DeclaredOnly will not include overrides methods if you will make ConfigureQueryFilters virtual.
    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(HeadlessEntityModelProcessor).GetMethod(
        nameof(_ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
    )!;

    private void _ConfigureQueryFilters<TEntity>(ModelBuilder builder, IMutableEntityType type)
        where TEntity : class
    {
        // Note: filters are applied once, so the expressions is cached, and it should depend
        //       on a properties and not cached values.

        var entityType = typeof(TEntity);
        var entityBuilder = builder.Entity<TEntity>();

        if (entityType.IsAssignableTo<IMultiTenant>())
        {
            var tenantIdName = _GetColumnName(entityBuilder.Metadata, nameof(IMultiTenant.TenantId));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.MultiTenancyFilter,
                x => EF.Property<string?>(x, tenantIdName) == TenantId
            );
        }

        if (entityType.IsAssignableTo<IDeleteAudit>())
        {
            var isDeletedName = _GetColumnName(entityBuilder.Metadata, nameof(IDeleteAudit.IsDeleted));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotDeletedFilter,
                x => !EF.Property<bool>(x, isDeletedName)
            );
        }

        if (entityType.IsAssignableTo<ISuspendAudit>())
        {
            var isSuspendedName = _GetColumnName(entityBuilder.Metadata, nameof(ISuspendAudit.IsSuspended));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotSuspendedFilter,
                x => !EF.Property<bool>(x, isSuspendedName)
            );
        }
    }

    private static string _GetColumnName(IMutableEntityType type, string name)
    {
        return type.FindProperty(name)?.GetColumnName() ?? name;
    }

    #endregion

    #region Process Entries

    public ProcessBeforeSaveReport ProcessEntries(DbContext db)
    {
        var report = new ProcessBeforeSaveReport();

        var currentUserId = currentUser.UserId;
        var currentAccountId = currentUser.AccountId;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            _ProcessEntry(entry, currentUserId, currentAccountId);

            if (entry.Entity is IDistributedMessageEmitter distributedMessageEmitter)
            {
                var messages = distributedMessageEmitter.GetDistributedMessages();

                if (messages.Count > 0)
                {
                    report.DistributedEmitters.Add(new(distributedMessageEmitter, messages));
                }
            }

            if (entry.Entity is ILocalMessageEmitter localMessageEmitter)
            {
                var messages = localMessageEmitter.GetLocalMessages();

                if (messages.Count > 0)
                {
                    report.LocalEmitters.Add(new(localMessageEmitter, messages));
                }
            }
        }

        return report;
    }

    private void _ProcessEntry(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetGuidId(entry);
                _TrySetMultiTenantId(entry);
                _TrySetCreateAudit(entry, currentUserId, currentAccountId);
                _TrySetConcurrencyStamp(entry);
                _TryPublishCreatedLocalMessage(entry);

                break;
            case EntityState.Modified:
                _TrySetUpdateAudit(entry, currentUserId, currentAccountId);
                _TrySetDeleteAudit(entry, currentUserId, currentAccountId);
                _TrySetSuspendAudit(entry, currentUserId, currentAccountId);
                _TryUpdateConcurrencyStamp(entry);
                _TryPublishUpdatedLocalMessage(entry);

                break;
            case EntityState.Deleted:
                _TryPublishDeletedLocalMessage(entry);

                break;
        }
    }

    #endregion

    #region Process Properties

    private void _TrySetMultiTenantId(EntityEntry entry)
    {
        if (entry.Entity is not IMultiTenant entity || !string.IsNullOrEmpty(entity.TenantId))
        {
            return;
        }

        if (entry.Property(nameof(IMultiTenant.TenantId)) is { IsModified: true, CurrentValue: not (null or "") })
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.TenantId, () => currentTenant.Id);
    }

    private void _TrySetGuidId(EntityEntry entry)
    {
        if (entry.Entity is not IEntity<Guid> entity || entity.Id != Guid.Empty)
        {
            return;
        }

        var idProperty = entry.Property(nameof(IEntity<>.Id)).Metadata.PropertyInfo!;

        if (
            idProperty.GetFirstOrDefaultAttribute<DatabaseGeneratedAttribute>() is
            { DatabaseGeneratedOption: not DatabaseGeneratedOption.None }
        )
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.Id, guidGenerator.Create);
    }

    private void _TrySetCreateAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is ICreateAudit entity)
        {
            _TrySetCreateAuditDate(entity);
            _TrySetCreateAuditId(entry, currentUserId, currentAccountId);
        }
    }

    private void _TrySetCreateAuditDate(ICreateAudit entity)
    {
        if (entity.DateCreated == default)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateCreated, () => clock.UtcNow);
        }
    }

    private static void _TrySetCreateAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        // No current user
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        // If the entity does not ICreateAudit<UserId> and not ICreateAudit<AccountId>, do not proceed.
        var byUser = entry.Entity as ICreateAudit<UserId>;
        var byAccount = entry.Entity as ICreateAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        // If CreatedById is already set as modified, do not proceed.

        if (entry.Property(nameof(ICreateAudit<>.CreatedById)) is { IsModified: true, CurrentValue: not null })
        {
            return;
        }

        // If CreatedBy navigation is present and already set, do not proceed.
        var createdBy = entry.Navigations.FirstOrDefault(p =>
            string.Equals(p.Metadata.Name, nameof(ICreateAudit<,>.CreatedBy), StringComparison.Ordinal)
        );

        if (createdBy?.CurrentValue is not null)
        {
            return;
        }

        if (byUser is not null && byUser.CreatedById == null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.CreatedById, () => currentUserId);

            return;
        }

        if (byAccount is not null && byAccount.CreatedById == null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.CreatedById, () => currentAccountId);
        }
    }

    private void _TrySetUpdateAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is not IUpdateAudit entity)
        {
            return;
        }

        _TrySetUpdateAuditDate(entry, entity);
        _TrySetUpdateAuditId(entry, currentUserId, currentAccountId);
    }

    private void _TrySetUpdateAuditDate(EntityEntry entry, IUpdateAudit entity)
    {
        var propertyEntry = entry.Property(nameof(IUpdateAudit.DateUpdated));

        if (
            entity.DateUpdated != null
            && propertyEntry.IsModified
            && propertyEntry.CurrentValue != propertyEntry.OriginalValue
        )
        {
            // If the property is modified, we do not set it again.
            return;
        }

        if (ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateUpdated, () => clock.UtcNow))
        {
            // If the property was successfully set, mark it as modified.
            propertyEntry.IsModified = true;
        }
    }

    private static void _TrySetUpdateAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        // No current user
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        // If the entity does not IUpdateAudit<UserId> and not IUpdateAudit<AccountId>, do not proceed.
        var byUser = entry.Entity as IUpdateAudit<UserId>;
        var byAccount = entry.Entity as IUpdateAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        // If UpdatedById is already set as modified, do not proceed.
        var propertyEntry = entry.Property(nameof(IUpdateAudit<>.UpdatedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.UpdatedById is null && currentUserId is not null)
        {
            if (ObjectPropertiesHelper.TrySetProperty(byUser, x => x.UpdatedById, () => currentUserId))
            {
                // If the property was successfully set, mark it as modified.
                propertyEntry.IsModified = true;
            }

            return;
        }

        if (byAccount is not null && byAccount.UpdatedById is null && currentAccountId is not null)
        {
            if (ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.UpdatedById, () => currentAccountId))
            {
                // If the property was successfully set, mark it as modified.
                propertyEntry.IsModified = true;
            }
        }
    }

    private bool _TrySetDeleteAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (entry.Entity is not IDeleteAudit deleteAudit || !entry.Property(nameof(IDeleteAudit.IsDeleted)).IsModified)
        {
            return false;
        }

        if (deleteAudit.IsDeleted)
        {
            _TrySetDeleteAuditDate(entry, deleteAudit);
            _TrySetDeleteAuditId(entry, currentUserId, currentAccountId);

            return true;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit.DateDeleted));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(IDeleteAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(deleteAudit, nameof(IDeleteAudit<>.DeletedById));
        }

        return true;
    }

    private void _TrySetDeleteAuditDate(EntityEntry entry, IDeleteAudit entity)
    {
        if (entity.DateDeleted == null || !entry.Property(nameof(IDeleteAudit.DateDeleted)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateDeleted, () => clock.UtcNow);
        }
    }

    private static void _TrySetDeleteAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        // No current user
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        // If the entity does not IDeleteAudit<UserId> and not IDeleteAudit<AccountId>, do not proceed.
        var byUser = entry.Entity as IDeleteAudit<UserId>;
        var byAccount = entry.Entity as IDeleteAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        // If UpdatedById is already set as modified, do not proceed.
        var propertyEntry = entry.Property(nameof(IDeleteAudit<>.DeletedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.DeletedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.DeletedById, () => currentUserId);
        }

        if (byAccount is not null && byAccount.DeletedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.DeletedById, () => currentAccountId);
        }
    }

    private bool _TrySetSuspendAudit(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        if (
            entry.Entity is not ISuspendAudit suspendAudit
            || !entry.Property(nameof(ISuspendAudit.IsSuspended)).IsModified
        )
        {
            return false;
        }

        if (suspendAudit.IsSuspended)
        {
            _TrySetSuspendAuditDate(entry, suspendAudit);
            _TrySetSuspendAuditId(entry, currentUserId, currentAccountId);

            return true;
        }

        ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit.DateSuspended));

        if (_ImplementsGenericInterface(entry.Entity.GetType(), typeof(ISuspendAudit<>)))
        {
            ObjectPropertiesHelper.TrySetPropertyToNull(suspendAudit, nameof(ISuspendAudit<>.SuspendedById));
        }

        return true;
    }

    private void _TrySetSuspendAuditDate(EntityEntry entry, ISuspendAudit entity)
    {
        if (entity.DateSuspended == null || !entry.Property(nameof(ISuspendAudit.DateSuspended)).IsModified)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.DateSuspended, () => clock.UtcNow);
        }
    }

    private static void _TrySetSuspendAuditId(EntityEntry entry, UserId? currentUserId, AccountId? currentAccountId)
    {
        // No current user
        if (currentUserId is null && currentAccountId is null)
        {
            return;
        }

        // If the entity does not ISuspendAudit<UserId> and not ISuspendAudit<AccountId>, do not proceed.
        var byUser = entry.Entity as ISuspendAudit<UserId>;
        var byAccount = entry.Entity as ISuspendAudit<AccountId>;

        if (byUser is null && byAccount is null)
        {
            return;
        }

        // If SuspendedById is already set as modified, do not proceed.
        var propertyEntry = entry.Property(nameof(ISuspendAudit<>.SuspendedById));

        if (propertyEntry.IsModified && propertyEntry.CurrentValue != propertyEntry.OriginalValue)
        {
            return;
        }

        if (byUser is not null && byUser.SuspendedById is null && currentUserId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byUser, x => x.SuspendedById, () => currentUserId);
        }

        if (byAccount is not null && byAccount.SuspendedById is null && currentAccountId is not null)
        {
            ObjectPropertiesHelper.TrySetProperty(byAccount, x => x.SuspendedById, () => currentAccountId);
        }
    }

    private static void _TrySetConcurrencyStamp(EntityEntry entry)
    {
        if (entry.Entity is IHasConcurrencyStamp { ConcurrencyStamp: null } entity)
        {
            ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
        }
    }

    private static void _TryUpdateConcurrencyStamp(EntityEntry entry)
    {
        if (entry.Entity is not IHasConcurrencyStamp entity)
        {
            return;
        }

        var propertyEntry = entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp));

        if (!string.Equals(propertyEntry.OriginalValue as string, entity.ConcurrencyStamp, StringComparison.Ordinal))
        {
            propertyEntry.OriginalValue = entity.ConcurrencyStamp;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.ConcurrencyStamp, () => Guid.NewGuid().ToString("N"));
    }

    #endregion

    #region Event Publishing

    private static void _TryPublishCreatedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is ILocalMessageEmitter localEmitter)
        {
            _PublishEntityCreated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishUpdatedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is ILocalMessageEmitter localEmitter && _HasDomainModifiedProperties(entry))
        {
            _PublishEntityUpdated(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _TryPublishDeletedLocalMessage(EntityEntry entry)
    {
        if (entry.Entity is ILocalMessageEmitter localEmitter)
        {
            _PublishEntityDeleted(localEmitter);
            _PublishEntityChanged(localEmitter);
        }
    }

    private static void _PublishEntityCreated(ILocalMessageEmitter entity)
    {
        var eventType = typeof(EntityCreatedEventData<>).MakeGenericType(entity.GetType());
        var eventMessage = (ILocalMessage)Activator.CreateInstance(eventType, entity)!;
        entity.AddMessage(eventMessage);
    }

    private static void _PublishEntityUpdated(ILocalMessageEmitter entity)
    {
        var eventType = typeof(EntityUpdatedEventData<>).MakeGenericType(entity.GetType());
        var eventMessage = (ILocalMessage)Activator.CreateInstance(eventType, entity)!;
        entity.AddMessage(eventMessage);
    }

    private static void _PublishEntityDeleted(ILocalMessageEmitter entity)
    {
        var eventType = typeof(EntityDeletedEventData<>).MakeGenericType(entity.GetType());
        var eventMessage = (ILocalMessage)Activator.CreateInstance(eventType, entity)!;
        entity.AddMessage(eventMessage);
    }

    private static void _PublishEntityChanged(ILocalMessageEmitter entity)
    {
        var eventType = typeof(EntityChangedEventData<>).MakeGenericType(entity.GetType());
        var eventMessage = (ILocalMessage)Activator.CreateInstance(eventType, entity)!;
        entity.AddMessage(eventMessage);
    }

    #endregion

    #region Helper Methods

    private static bool _HasDomainModifiedProperties(EntityEntry entry)
    {
        // Currently: This method ignores changes to navigation properties and foreign keys.
        return entry.Properties.Any(_IsHasDomainModified);
    }

    private static bool _IsHasDomainModified(PropertyEntry property)
    {
        return property.IsModified
            // The property is not generated by the database on update (e.g., not an auto-increment or computed column).
            && property.Metadata.ValueGenerated is ValueGenerated.Never or ValueGenerated.OnAdd
            // The property is not a foreign key (so navigation/relationship changes are ignored).
            && !property.Metadata.IsForeignKey();
    }

    private static bool _ImplementsGenericInterface(Type type, Type genericInterfaceDefinition)
    {
        return type.GetInterfaces()
            .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericInterfaceDefinition);
    }

    #endregion
}
