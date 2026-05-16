// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tests.Entities;
using Tests.Fixtures;

namespace Tests.Fixture;

/// <summary>
/// Test HeadlessIdentityDbContext implementation that captures emitted messages for verification.
/// </summary>
public sealed class TestIdentityDbContext(
    HeadlessDbContextServices services,
    RecordingHeadlessMessageDispatcher messageDispatcher,
    DbContextOptions options
)
    : HeadlessIdentityDbContext<
        TestUser,
        TestRole,
        string,
        IdentityUserClaim<string>,
        IdentityUserRole<string>,
        IdentityUserLogin<string>,
        IdentityRoleClaim<string>,
        IdentityUserToken<string>,
        IdentityUserPasskey<string>
    >(services, options),
        IHarnessDbContext
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
