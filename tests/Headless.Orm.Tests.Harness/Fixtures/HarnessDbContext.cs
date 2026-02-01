// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tests.Entities;

namespace Tests.Fixtures;

/// <summary>
/// Test HeadlessDbContext implementation that captures emitted messages for verification.
/// </summary>
public sealed class HarnessDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
    : HeadlessDbContext(entityProcessor, options),
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
}
