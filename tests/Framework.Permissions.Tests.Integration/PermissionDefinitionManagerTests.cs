// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionDefinitionManagerTests(PermissionsTestFixture fixture, ITestOutputHelper output)
    : PermissionsTestBase(fixture, output)
{
    [Fact]
    public async Task should_get_defined_settings_when_call_GetAllAsync_and_is_defined()
    {
        // given
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();

        // when
        var groups = await definitionManager.GetGroupsAsync();
        var definitions = await definitionManager.GetPermissionsAsync();

        // then
        definitions.Should().NotBeEmpty();
        groups.Should().NotBeEmpty();
        groups.Should().ContainSingle();
        definitions.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_get_defined_setting_when_call_GetOrDefaultAsync_and_is_defined()
    {
        // given
        using var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<PermissionsDefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var definitions = await definitionManager.GetPermissionsAsync();
        var existDefinition = definitions[0];

        // when
        var definition = await definitionManager.GetOrDefaultAsync(existDefinition.Name);

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
            var group = context.AddGeneratedPermissionGroup();
            group.AddGeneratedPermissionDefinition();
            group.AddGeneratedPermissionDefinition();
            group.AddGeneratedPermissionDefinition();
        }
    }
}
