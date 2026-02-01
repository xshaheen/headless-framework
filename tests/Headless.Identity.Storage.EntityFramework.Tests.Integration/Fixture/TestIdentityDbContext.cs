// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Orm.EntityFramework;
using Headless.Orm.EntityFramework.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tests.Entities;
using Tests.Fixtures;

namespace Tests.Fixture;

/// <summary>
/// Test HeadlessIdentityDbContext implementation that captures emitted messages for verification.
/// </summary>
public sealed class TestIdentityDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
    : HeadlessIdentityDbContext<
        TestUser,
        TestRole,
        string,
        IdentityUserClaim<string>,
        IdentityUserRole<string>,
        IdentityUserLogin<string>,
        IdentityRoleClaim<string>,
        IdentityUserToken<string>
    >(entityProcessor, options),
        IHarnessDbContext
{
    public DbSet<HarnessTestEntity> TestEntities { get; set; } = null!;

    public DbSet<HarnessBasicEntity> BasicEntities { get; set; } = null!;

    public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

    public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

    public override string DefaultSchema => "";

    protected override Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
        return Task.CompletedTask;
    }

    protected override void PublishMessages(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
    }

    protected override Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedLocalMessages.AddRange(emitters);
        return Task.CompletedTask;
    }

    protected override void PublishMessages(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedLocalMessages.AddRange(emitters);
    }

    /// <summary>
    /// Clears all captured messages. Useful for test cleanup between operations.
    /// </summary>
    public void ClearCapturedMessages()
    {
        EmittedDistributedMessages.Clear();
        EmittedLocalMessages.Clear();
    }

    /// <summary>
    /// Explicit implementation for IHarnessDbContext. The cancellation token is not used.
    /// </summary>
    Task IHarnessDbContext.ExecuteTransactionAsync(
        Func<Task<bool>> operation,
        IsolationLevel isolation,
        CancellationToken cancellationToken
    )
    {
        return ExecuteTransactionAsync(operation, isolation);
    }
}
