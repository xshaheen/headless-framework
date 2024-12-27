// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

[Collection(nameof(PermissionsTestFixture))]
public sealed class DynamicPermissionDefinitionStoreTests(PermissionsTestFixture fixture)
{
    [Fact]
    public async Task should_save_defined_settings_when_call_SaveAsync()
    {
        // given
        var hostBuilder = _CreatePermissionsHostBuilder();

        hostBuilder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        var host = hostBuilder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();
        var groupsBefore = await store.GetGroupsAsync();
        var definitionsBefore = await store.GetPermissionsAsync();

        // when
        await store.SaveAsync();
        var definitionsAfter = await store.GetPermissionsAsync();
        var groupsAfter = await store.GetGroupsAsync();

        // then
        definitionsBefore.Should().BeEmpty();
        groupsBefore.Should().BeEmpty();

        definitionsAfter.Should().NotBeEmpty();
        definitionsAfter.Should().HaveCount(3);

        groupsAfter.Should().NotBeEmpty();
        groupsAfter.Should().ContainSingle();
    }

    [UsedImplicitly]
    private sealed class PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGeneratedPermissionGroup();
            group.AddGeneratedPermissionDefinition();
            group.AddGeneratedPermissionDefinition();
            group.AddGeneratedPermissionDefinition();
        }
    }

    private HostApplicationBuilder _CreatePermissionsHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>();
        builder.Services.ConfigurePermissionsServices(fixture.ConnectionString);

        return builder;
    }
}
