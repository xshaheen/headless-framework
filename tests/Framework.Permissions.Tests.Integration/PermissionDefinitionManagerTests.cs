// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionDefinitionManagerTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private static readonly PermissionGroupDefinition[] _GroupDefinitions =
    [
        TestData.CreateGroupDefinition(4),
        TestData.CreateGroupDefinition(5),
        TestData.CreateGroupDefinition(7),
    ];

    [Fact]
    public async Task should_get_empty_when_call_GetAllAsync_and_no_definitions()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync(AbortToken);
        var permissions = await definitionManager.GetPermissionsAsync(AbortToken);

        // then
        groups.Should().BeEmpty();
        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync(AbortToken);
        var definitions = await definitionManager.GetPermissionsAsync(AbortToken);

        // then
        groups.Should().HaveCount(3);
        groups.Should().BeEquivalentTo(_GroupDefinitions);
        definitions.Should().HaveCount(16);
        definitions.Should().BeEquivalentTo(_GroupDefinitions.SelectMany(x => x.Permissions));
    }

    [Fact]
    public async Task should_get_default_when_call_GetOrDefaultAsync_and_is_not_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var randomSettingName = Faker.Random.String2(5, 10);

        // when
        var definition = await definitionManager.FindAsync(randomSettingName, AbortToken);

        // then
        definition.Should().BeNull();
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        await Fixture.ResetAsync();
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var definitions = await definitionManager.GetPermissionsAsync(AbortToken);
        var existDefinition = definitions[0];

        // when
        var definition = await definitionManager.FindAsync(existDefinition.Name, AbortToken);

        // then
        definition.Should().NotBeNull();
        definition.Name.Should().Be(existDefinition.Name);
        definition.DisplayName.Should().Be(existDefinition.DisplayName);
        definition.IsEnabled.Should().Be(existDefinition.IsEnabled);
    }

    [UsedImplicitly]
    private sealed class PermissionsDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            foreach (var item in _GroupDefinitions)
            {
                context.AddGroup(item);
            }
        }
    }
}
