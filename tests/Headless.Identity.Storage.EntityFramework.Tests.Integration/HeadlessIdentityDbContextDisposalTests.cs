// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;
using Tests.Tests;

namespace Tests;

public sealed class HeadlessIdentityDbContextDisposalTests : HeadlessDbContextDisposalTestBase<TestIdentityDbContext>
{
    protected override void ConfigureContext(IServiceCollection services)
    {
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(options => options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused"));
    }
}
