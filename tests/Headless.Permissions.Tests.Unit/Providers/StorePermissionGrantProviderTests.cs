// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Providers;

public sealed class StorePermissionGrantProviderTests : TestBase
{
    private readonly IPermissionGrantStore _grantStore;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestStorePermissionGrantProvider _sut;

    public StorePermissionGrantProviderTests()
    {
        _grantStore = Substitute.For<IPermissionGrantStore>();
        _currentTenant = Substitute.For<ICurrentTenant>();
        _sut = new TestStorePermissionGrantProvider(_grantStore, _currentTenant);
    }

    [Fact]
    public async Task should_delegate_single_permission_check_to_multiple_check()
    {
        // given
        var permission = CreatePermission("Users.Create");
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Roles.Returns(new HashSet<string>());

        // when
        var result = await _sut.CheckAsync(permission, currentUser, AbortToken);

        // then
        result.Status.Should().Be(PermissionGrantStatus.Undefined);
        _sut.CheckAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task should_grant_permission_with_tenant_context()
    {
        // given
        var permission = CreatePermission("Users.Create");
        const string providerKey = "admin-role";
        const string tenantId = "tenant-1";
        _currentTenant.Id.Returns(tenantId);

        // when
        await _sut.SetAsync(permission, providerKey, isGranted: true, AbortToken);

        // then
        await _grantStore.Received(1).GrantAsync("Users.Create", "TestProvider", providerKey, tenantId, AbortToken);
    }

    [Fact]
    public async Task should_revoke_permission_via_store()
    {
        // given
        var permission = CreatePermission("Users.Create");
        const string providerKey = "admin-role";

        // when
        await _sut.SetAsync(permission, providerKey, isGranted: false, AbortToken);

        // then
        await _grantStore.Received(1).RevokeAsync("Users.Create", "TestProvider", providerKey, AbortToken);
    }

    private static PermissionDefinition CreatePermission(string name) => new(name);

    /// <summary>Test implementation to verify base class behavior.</summary>
    private sealed class TestStorePermissionGrantProvider(
        IPermissionGrantStore grantStore,
        ICurrentTenant currentTenant
    ) : StorePermissionGrantProvider(grantStore, currentTenant)
    {
        public int CheckAsyncCallCount { get; private set; }

        public override string Name => "TestProvider";

        public override Task<MultiplePermissionGrantStatusResult> CheckAsync(
            IReadOnlyCollection<PermissionDefinition> permissions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken = default
        )
        {
            CheckAsyncCallCount++;
            var names = permissions.Select(p => p.Name).ToList();
            var roles = currentUser.Roles;
            return Task.FromResult(
                new MultiplePermissionGrantStatusResult(names, roles, PermissionGrantStatus.Undefined)
            );
        }
    }
}
