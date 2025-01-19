// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class DynamicPermissionDefinitionStoreTests(PermissionsTestFixture fixture, ITestOutputHelper output)
    : PermissionsTestBase(fixture, output)
{
    private static readonly PermissionGroupDefinition _GroupDefinition = TestData.CreateGroupDefinition();

    [Fact]
    public async Task should_save_defined_permissions()
    {
        // given
        var builder = CreateHostBuilder();
        builder.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>();

        builder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        var host = builder.Build();

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
        definitionsAfter.Should().HaveCount(3);
        groupsAfter.Should().ContainSingle();
        groupsAfter[0].Should().BeEquivalentTo(_GroupDefinition);
    }

    [UsedImplicitly]
    private sealed class PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context) => context.AddGroup(_GroupDefinition);
    }
}
