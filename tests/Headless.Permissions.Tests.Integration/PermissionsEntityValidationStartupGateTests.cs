// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionsEntityValidationStartupGateTests(PermissionsTestFixture fixture)
    : PermissionsTestBase(fixture)
{
    [Fact]
    public async Task should_fail_startup_when_shared_dbcontext_does_not_include_permissions_entities()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        // AddHeadlessPermissions auto-registers the management core, whose initialization hosted
        // service requires TimeProvider — register it so startup reaches the entity-validation gate
        // rather than failing to activate the hosted service first.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContextFactory<MissingPermissionsEntityDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
        builder.Services.AddHeadlessPermissions(setup =>
            setup.UseEntityFramework<MissingPermissionsEntityDbContext>()
        );
        using var host = builder.Build();

        // when
        var action = async () => await host.StartAsync(AbortToken);

        // then
        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*PermissionGrantRecord*modelBuilder.AddHeadlessPermissions*");
    }

    private sealed class MissingPermissionsEntityDbContext(
        DbContextOptions<MissingPermissionsEntityDbContext> options
    ) : DbContext(options);
}
