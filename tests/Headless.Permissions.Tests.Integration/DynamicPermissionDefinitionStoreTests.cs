// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class DynamicPermissionDefinitionStoreTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private static readonly PermissionGroupDefinition _GroupDefinition = TestData.CreateGroupDefinition();

    [Fact]
    public async Task should_save_defined_permissions()
    {
        // given
        await Fixture.ResetAsync();
        var builder = CreateHostBuilder();
        builder.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>();

        builder.Services.Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
            options.DynamicDefinitionsMemoryCacheExpiration = TimeSpan.Zero;
        });

        using var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDynamicPermissionDefinitionStore>();
        var groupsBefore = await store.GetGroupsAsync(AbortToken);
        var definitionsBefore = await store.GetPermissionsAsync(AbortToken);

        // when
        await store.SaveAsync(AbortToken);
        var definitionsAfter = await store.GetPermissionsAsync(AbortToken);
        var groupsAfter = await store.GetGroupsAsync(AbortToken);

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
