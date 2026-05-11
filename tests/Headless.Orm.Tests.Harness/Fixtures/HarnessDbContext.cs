// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore;
using Tests.Entities;

namespace Tests.Fixtures;

/// <summary>
/// Test HeadlessDbContext implementation that captures emitted messages for verification.
/// </summary>
public sealed class HarnessDbContext(
    HeadlessDbContextServices services,
    RecordingHeadlessMessageDispatcher messageDispatcher,
    DbContextOptions options
) : HeadlessDbContext(services, options), IHarnessDbContext
{
    public DbSet<HarnessTestEntity> TestEntities { get; set; } = null!;

    public DbSet<HarnessBasicEntity> BasicEntities { get; set; } = null!;

    public List<EmitterDistributedMessages> EmittedDistributedMessages => messageDispatcher.EmittedDistributedMessages;

    public List<EmitterLocalMessages> EmittedLocalMessages => messageDispatcher.EmittedLocalMessages;

    public override string DefaultSchema => "";

    /// <summary>
    /// Clears all captured messages. Useful for test cleanup between operations.
    /// </summary>
    public void ClearCapturedMessages()
    {
        EmittedDistributedMessages.Clear();
        EmittedLocalMessages.Clear();
    }
}
