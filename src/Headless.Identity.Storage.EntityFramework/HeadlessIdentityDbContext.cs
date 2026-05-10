// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Orm.EntityFramework;

public abstract class HeadlessIdentityDbContext<
    TUser,
    TRole,
    TKey,
    TUserClaim,
    TUserRole,
    TUserLogin,
    TRoleClaim,
    TUserToken
> : IdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>
{
    private readonly HeadlessDbContextRuntime _runtime;

    public abstract string DefaultSchema { get; }

    protected HeadlessIdentityDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
        : base(options)
    {
        _runtime = new(this, entityProcessor);
        _runtime.SyncNavigationTracker();
    }

    #region Core Save Changes

    protected virtual async Task<int> CoreSaveChangesAsync(
        bool acceptAllChangesOnSuccess = true,
        CancellationToken cancellationToken = default
    )
    {
        return await _runtime
            .SaveChangesAsync(
                PublishMessagesAsync,
                PublishMessagesAsync,
                _BaseSaveChangesAsync,
                acceptAllChangesOnSuccess,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    protected virtual int CoreSaveChanges(bool acceptAllChangesOnSuccess = true)
    {
        return _runtime.SaveChanges(PublishMessages, PublishMessages, _BaseSaveChanges, acceptAllChangesOnSuccess);
    }

    #endregion

    #region Overrides Save Changes

    public override int SaveChanges()
    {
        return CoreSaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return CoreSaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new())
    {
        return CoreSaveChangesAsync(cancellationToken: cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = new()
    )
    {
        return CoreSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private Task<int> _BaseSaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private int _BaseSaveChanges(bool acceptAllChangesOnSuccess)
    {
#pragma warning disable MA0045 // Do not use blocking calls in a sync method (need to make calling method async)
        return base.SaveChanges(acceptAllChangesOnSuccess);
#pragma warning restore MA0045
    }

    #endregion

    #region Publish Messages

    protected abstract Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    protected abstract void PublishMessages(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    );

    protected abstract Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    );

    protected abstract void PublishMessages(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction
    );

    #endregion

    #region Configure Conventions

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        HeadlessDbContextRuntime.ConfigureConventions(configurationBuilder);
    }

    #endregion

    #region Model Creating

    protected override void OnModelCreating(ModelBuilder builder)
    {
        HeadlessDbContextRuntime.ConfigureDefaultSchema(builder, DefaultSchema);
        base.OnModelCreating(builder);
        _runtime.ProcessModelCreating(builder);
    }

    #endregion
}
