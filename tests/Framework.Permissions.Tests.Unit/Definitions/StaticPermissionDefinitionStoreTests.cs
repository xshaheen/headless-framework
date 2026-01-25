// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tests.Definitions;

public sealed class StaticPermissionDefinitionStoreTests : TestBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly PermissionManagementProvidersOptions _providersOptions;

    public StaticPermissionDefinitionStoreTests()
    {
        _serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
        _providersOptions = new PermissionManagementProvidersOptions();
    }

    #region GetAllPermissionsAsync

    [Fact]
    public async Task should_load_permissions_from_all_providers()
    {
        // given
        var provider1 = new TestPermissionProvider("Group1", ["Permission1", "Permission2"]);
        var provider2 = new TestPermissionProvider("Group2", ["Permission3"]);
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider>();
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider2>();
        var sut = _CreateStoreWithProviders(provider1, provider2);

        // when
        var permissions = await sut.GetAllPermissionsAsync(AbortToken);

        // then
        permissions.Should().HaveCount(3);
        permissions.Select(p => p.Name).Should().Contain(["Permission1", "Permission2", "Permission3"]);
    }

    [Fact]
    public async Task should_cache_static_permissions()
    {
        // given
        var provider = new TestPermissionProvider("Group1", ["Permission1"]);
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider>();
        var sut = _CreateStoreWithProviders(provider);

        // when
        var firstCall = await sut.GetAllPermissionsAsync(AbortToken);
        var secondCall = await sut.GetAllPermissionsAsync(AbortToken);

        // then
        firstCall.Should().BeSameAs(secondCall);
    }

    [Fact]
    public async Task should_return_permission_by_name()
    {
        // given
        var provider = new TestPermissionProvider("Group1", ["Permission1", "Permission2"]);
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider>();
        var sut = _CreateStoreWithProviders(provider);

        // when
        var permission = await sut.GetOrDefaultPermissionAsync("Permission1", AbortToken);

        // then
        permission.Should().NotBeNull();
        permission!.Name.Should().Be("Permission1");
    }

    [Fact]
    public async Task should_return_null_for_unknown_permission()
    {
        // given
        var provider = new TestPermissionProvider("Group1", ["Permission1"]);
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider>();
        var sut = _CreateStoreWithProviders(provider);

        // when
        var permission = await sut.GetOrDefaultPermissionAsync("Unknown", AbortToken);

        // then
        permission.Should().BeNull();
    }

    #endregion

    #region GetGroupsAsync

    [Fact]
    public async Task should_return_all_groups()
    {
        // given
        var provider1 = new TestPermissionProvider("Group1", ["Permission1"]);
        var provider2 = new TestPermissionProvider("Group2", ["Permission2"]);
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider>();
        _providersOptions.DefinitionProviders.Add<TestPermissionProvider2>();
        var sut = _CreateStoreWithProviders(provider1, provider2);

        // when
        var groups = await sut.GetGroupsAsync(AbortToken);

        // then
        groups.Should().HaveCount(2);
        groups.Select(g => g.Name).Should().Contain(["Group1", "Group2"]);
    }

    [Fact]
    public async Task should_return_all_permissions_flat()
    {
        // given
        var provider = new TestPermissionProviderWithChildren();
        _providersOptions.DefinitionProviders.Add<TestPermissionProviderWithChildren>();
        var sut = _CreateStoreWithChildrenProvider(provider);

        // when
        var permissions = await sut.GetAllPermissionsAsync(AbortToken);

        // then
        permissions.Should().HaveCount(3);
        permissions.Select(p => p.Name).Should().Contain(["Parent", "Child1", "Child2"]);
    }

    #endregion

    #region Helpers

    private StaticPermissionDefinitionStore _CreateStoreWithProviders(params IPermissionDefinitionProvider[] providers)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        _serviceScopeFactory.CreateScope().Returns(scope);

        // Map provider types to instances
        for (var i = 0; i < providers.Length; i++)
        {
            var providerType =
                i == 0 ? typeof(TestPermissionProvider)
                : i == 1 ? typeof(TestPermissionProvider2)
                : typeof(TestPermissionProviderWithChildren);

            serviceProvider.GetService(providerType).Returns(providers[i]);
        }

        var optionsAccessor = Substitute.For<IOptions<PermissionManagementProvidersOptions>>();
        optionsAccessor.Value.Returns(_providersOptions);

        return new StaticPermissionDefinitionStore(_serviceScopeFactory, optionsAccessor);
    }

    private StaticPermissionDefinitionStore _CreateStoreWithChildrenProvider(
        TestPermissionProviderWithChildren provider
    )
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        _serviceScopeFactory.CreateScope().Returns(scope);

        serviceProvider.GetService(typeof(TestPermissionProviderWithChildren)).Returns(provider);

        var optionsAccessor = Substitute.For<IOptions<PermissionManagementProvidersOptions>>();
        optionsAccessor.Value.Returns(_providersOptions);

        return new StaticPermissionDefinitionStore(_serviceScopeFactory, optionsAccessor);
    }

    private sealed class TestPermissionProvider(string groupName, string[] permissionNames)
        : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup(groupName);
            foreach (var name in permissionNames)
            {
                group.AddChild(name);
            }
        }
    }

    private sealed class TestPermissionProvider2 : IPermissionDefinitionProvider
    {
        private readonly string _groupName;
        private readonly string[] _permissionNames;

        public TestPermissionProvider2()
        {
            _groupName = "Group2";
            _permissionNames = ["Permission3"];
        }

        public TestPermissionProvider2(string groupName, string[] permissionNames)
        {
            _groupName = groupName;
            _permissionNames = permissionNames;
        }

        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup(_groupName);
            foreach (var name in _permissionNames)
            {
                group.AddChild(name);
            }
        }
    }

    private sealed class TestPermissionProviderWithChildren : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup("GroupWithChildren");
            var parent = group.AddChild("Parent");
            parent.AddChild("Child1");
            parent.AddChild("Child2");
        }
    }

    #endregion
}
