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
    >(options),
        IHarnessDbContext
{
    public DbSet<HarnessTestEntity> TestEntities { get; set; } = null!;

    public DbSet<HarnessBasicEntity> BasicEntities { get; set; } = null!;

    public List<object> EmittedDistributedMessages => messageDispatcher.EmittedDistributedMessages;

    public List<object> EmittedLocalMessages => messageDispatcher.EmittedLocalMessages;

    public override string DefaultSchema => "";

    // The harness filter suites verify the opt-in suspend filter, so the shared test entity opts in.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<HarnessTestEntity>().HasNotSuspendedFilter();
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
