// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Security;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class TenantRoutedContextGuardTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_refuse_only_a_tenant_routed_context(bool routed)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStringEncryptionService(options =>
        {
            options.DefaultPassPhrase = "TestPassPhrase123456";
            options.DefaultSalt = [.. "TestSalt"u8];
        });

        if (routed)
        {
            services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(GuardedDbContext)));
        }

        // when
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<GuardedDbContext>());
        await using var provider = services.BuildServiceProvider();
        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(new(provider, new TenantPostureManifest())))
            .Where(diagnostic => diagnostic.Code == "HEADLESS_TENANCY_ROUTED_CONTEXT_NOT_ALLOWED")
            .ToList();

        // then
        diagnostics.Should().HaveCount(routed ? 1 : 0);
    }

    private sealed class GuardedDbContext(DbContextOptions<GuardedDbContext> options) : DbContext(options);
}
