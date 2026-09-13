// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Tests;

namespace Tests;

public sealed class HeadlessDbContextDisposalTests : HeadlessDbContextDisposalTestBase<FactoryTestDbContext>
{
    protected override void ConfigureContext(IServiceCollection services)
    {
        services.AddHeadlessDbContext<FactoryTestDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        );
    }
}
