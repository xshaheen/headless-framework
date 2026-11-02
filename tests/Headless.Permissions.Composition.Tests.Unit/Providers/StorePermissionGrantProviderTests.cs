// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Permissions;
using Headless.Testing.Tests;

namespace Tests.Providers;

public sealed class StorePermissionGrantProviderTests : TestBase
{
    private readonly IPermissionGrantStore _grantStore;
    private readonly TestStorePermissionGrantProvider _sut;

    public StorePermissionGrantProviderTests()
    {
        _grantStore = Substitute.For<IPermissionGrantStore>();
        _sut = new TestStorePermissionGrantProvider(_grantStore);
    }

    [Fact]
    public async Task should_delegate_single_permission_check_to_multiple_check()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.Roles.Returns(new HashSet<string>(StringComparer.Ordinal));

        // when
        var result = await _sut.CheckAsync(permission, currentUser, AbortToken);

        // then
        result.Status.Should().Be(PermissionGrantStatus.Undefined);
        _sut.CheckAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task should_grant_permission_via_store()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        const string providerKey = "admin-role";

        // when
        await _sut.SetAsync(permission, providerKey, isGranted: true, AbortToken);

        // then
        await _grantStore.Received(1).GrantAsync("Users.Create", "TestProvider", providerKey, AbortToken);
    }

    [Fact]
    public async Task should_grant_permissions_via_store_in_one_batch()
    {
        // given
        PermissionDefinition[] permissions = [_CreatePermission("Users.Create"), _CreatePermission("Users.Delete")];
        const string providerKey = "admin-role";

        // when
        await _sut.SetAsync(permissions, providerKey, isGranted: true, AbortToken);

        // then
        await _grantStore
            .Received(1)
            .GrantAsync(
                Arg.Is<IReadOnlyCollection<string>>(static names =>
                    names.SequenceEqual(new[] { "Users.Create", "Users.Delete" })
                ),
                "TestProvider",
                providerKey,
                AbortToken
            );
    }

    [Fact]
    public async Task should_revoke_permission_via_store()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        const string providerKey = "admin-role";

        // when
        await _sut.SetAsync(permission, providerKey, isGranted: false, AbortToken);

        // then
        await _grantStore.Received(1).RevokeAsync("Users.Create", "TestProvider", providerKey, AbortToken);
    }

    private static PermissionDefinition _CreatePermission(string name)
    {
        var group = new PermissionGroupDefinition("TestGroup");
        return group.AddChild(name);
    }

    /// <summary>Test implementation to verify base class behavior.</summary>
    private sealed class TestStorePermissionGrantProvider(IPermissionGrantStore grantStore)
        : StorePermissionGrantProvider(grantStore)
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
