// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Exceptions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Permissions.Resources;
using Headless.Primitives;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Grants;

public sealed class PermissionManagerTests : TestBase
{
    private readonly IPermissionDefinitionManager _definitionManager;
    private readonly IPermissionGrantProviderManager _grantProviderManager;
    private readonly IPermissionGrantRepository _repository;
    private readonly IPermissionErrorsDescriptor _errorsDescriptor;
    private readonly PermissionManager _sut;

    public PermissionManagerTests()
    {
        _definitionManager = Substitute.For<IPermissionDefinitionManager>();
        _grantProviderManager = Substitute.For<IPermissionGrantProviderManager>();
        _repository = Substitute.For<IPermissionGrantRepository>();
        _errorsDescriptor = Substitute.For<IPermissionErrorsDescriptor>();

        _sut = new PermissionManager(_definitionManager, _grantProviderManager, _repository, _errorsDescriptor);
    }

    #region GetAsync - Single Permission

    [Fact]
    public async Task should_return_not_granted_for_disabled_permission()
    {
        // given
        const string permissionName = "Users.Create";
        var currentUser = Substitute.For<ICurrentUser>();
        var disabledPermission = _CreatePermission(permissionName, isEnabled: false);

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(disabledPermission);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.Name.Should().Be(permissionName);
        result.IsGranted.Should().BeFalse();
        result.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_not_granted_for_undefined_permission()
    {
        // given
        const string permissionName = "NonExistent.Permission";
        var currentUser = Substitute.For<ICurrentUser>();

        _definitionManager.FindAsync(permissionName, AbortToken).Returns((PermissionDefinition?)null);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.Name.Should().Be(permissionName);
        result.IsGranted.Should().BeFalse();
        result.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_check_all_providers_for_permission()
    {
        // given
        const string permissionName = "Users.Create";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(permissionName);

        var provider1 = Substitute.For<IPermissionGrantProvider>();
        provider1.Name.Returns("Role");
        provider1
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([permissionName], ["admin"], PermissionGrantStatus.Undefined)
            );

        var provider2 = Substitute.For<IPermissionGrantProvider>();
        provider2.Name.Returns("User");
        provider2
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([permissionName], ["user-123"], PermissionGrantStatus.Granted)
            );

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([provider1, provider2]);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.IsGranted.Should().BeTrue();
        await provider1
            .Received(2)
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken);
        await provider2
            .Received(2)
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken);
    }

    [Fact]
    public async Task should_apply_explicit_deny_over_grant()
    {
        // given - AWS IAM semantics: explicit deny overrides grant
        const string permissionName = "Users.Delete";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(permissionName);

        var grantProvider = Substitute.For<IPermissionGrantProvider>();
        grantProvider.Name.Returns("Role");
        grantProvider
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([permissionName], ["admin"], PermissionGrantStatus.Granted)
            );

        var denyProvider = Substitute.For<IPermissionGrantProvider>();
        denyProvider.Name.Returns("User");
        denyProvider
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult(
                    [permissionName],
                    ["user-123"],
                    PermissionGrantStatus.Prohibited
                )
            );

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([grantProvider, denyProvider]);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.IsGranted.Should().BeFalse("explicit deny should override grant");
        result.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_granted_when_any_provider_grants()
    {
        // given
        const string permissionName = "Users.Read";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(permissionName);

        var provider1 = Substitute.For<IPermissionGrantProvider>();
        provider1.Name.Returns("Role");
        provider1
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([permissionName], ["admin"], PermissionGrantStatus.Granted)
            );

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([provider1]);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.IsGranted.Should().BeTrue();
    }

    [Fact]
    public async Task should_include_provider_info_in_result()
    {
        // given
        const string permissionName = "Users.Read";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(permissionName);

        var provider = Substitute.For<IPermissionGrantProvider>();
        provider.Name.Returns("Role");
        provider
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult(
                    [permissionName],
                    ["admin", "editor"],
                    PermissionGrantStatus.Granted
                )
            );

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([provider]);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, cancellationToken: AbortToken);

        // then
        result.Providers.Should().HaveCount(1);
        result.Providers[0].Name.Should().Be("Role");
        result.Providers[0].Keys.Should().BeEquivalentTo(["admin", "editor"]);
    }

    [Fact]
    public async Task should_filter_by_provider_name()
    {
        // given
        const string permissionName = "Users.Read";
        const string targetProvider = "Role";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(permissionName);

        var roleProvider = Substitute.For<IPermissionGrantProvider>();
        roleProvider.Name.Returns("Role");
        roleProvider
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([permissionName], ["admin"], PermissionGrantStatus.Granted)
            );

        var userProvider = Substitute.For<IPermissionGrantProvider>();
        userProvider.Name.Returns("User");

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([roleProvider, userProvider]);

        // when
        var result = await _sut.GetAsync(permissionName, currentUser, targetProvider, AbortToken);

        // then
        result.IsGranted.Should().BeTrue();
        await userProvider
            .DidNotReceive()
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken);
    }

    #endregion

    #region GetAllAsync - Multiple Permissions

    [Fact]
    public async Task should_validate_permission_names_not_null()
    {
        // given
        var currentUser = Substitute.For<ICurrentUser>();

        // when
        var act = () => _sut.GetAllAsync(null!, currentUser, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_validate_current_user_not_null()
    {
        // given
        string[] names = ["Users.Create"];

        // when
        var act = () => _sut.GetAllAsync(names, null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_return_empty_for_empty_names()
    {
        // given
        var currentUser = Substitute.For<ICurrentUser>();
        string[] emptyNames = [];

        // when
        var result = await _sut.GetAllAsync(emptyNames, currentUser, cancellationToken: AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_handle_undefined_in_batch()
    {
        // given
        const string definedPermission = "Users.Create";
        const string undefinedPermission = "NonExistent.Permission";
        var currentUser = Substitute.For<ICurrentUser>();
        var permission = _CreatePermission(definedPermission);

        var provider = Substitute.For<IPermissionGrantProvider>();
        provider.Name.Returns("Role");
        provider
            .CheckAsync(Arg.Any<IReadOnlyCollection<PermissionDefinition>>(), currentUser, AbortToken)
            .Returns(
                new MultiplePermissionGrantStatusResult([definedPermission], ["admin"], PermissionGrantStatus.Granted)
            );

        _definitionManager.FindAsync(definedPermission, AbortToken).Returns(permission);
        _definitionManager.FindAsync(undefinedPermission, AbortToken).Returns((PermissionDefinition?)null);
        _grantProviderManager.ValueProviders.Returns([provider]);

        // when
        var result = await _sut.GetAllAsync(
            [definedPermission, undefinedPermission],
            currentUser,
            cancellationToken: AbortToken
        );

        // then
        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Name == definedPermission && x.IsGranted);
        result.Should().Contain(x => x.Name == undefinedPermission && !x.IsGranted);
    }

    #endregion

    #region SetAsync - Single Permission

    [Fact]
    public async Task should_throw_when_setting_undefined()
    {
        // given
        const string permissionName = "NonExistent.Permission";
        var errorDescriptor = new ErrorDescriptor("test", "Permission not defined");

        _definitionManager.FindAsync(permissionName, AbortToken).Returns((PermissionDefinition?)null);
        _errorsDescriptor.PermissionIsNotDefined(permissionName).Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionName, "Role", "admin", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_throw_when_setting_disabled()
    {
        // given
        const string permissionName = "Users.Create";
        var disabledPermission = _CreatePermission(permissionName, isEnabled: false);
        var errorDescriptor = new ErrorDescriptor("test", "Permission disabled");

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(disabledPermission);
        _errorsDescriptor.PermissionDisabled(permissionName).Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionName, "Role", "admin", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_throw_when_provider_not_allowed()
    {
        // given
        const string permissionName = "Users.Create";
        const string providerName = "NotAllowedProvider";
        var permission = _CreatePermission(permissionName);
        permission.Providers.Add("Role");
        permission.Providers.Add("User");
        var errorDescriptor = new ErrorDescriptor("test", "Provider not allowed");

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _errorsDescriptor
            .PermissionProviderNotDefined(permissionName, providerName)
            .Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionName, providerName, "key", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_throw_when_provider_not_found()
    {
        // given
        const string permissionName = "Users.Create";
        const string providerName = "NonExistentProvider";
        var permission = _CreatePermission(permissionName);
        var errorDescriptor = new ErrorDescriptor("test", "Provider not found");

        _definitionManager.FindAsync(permissionName, AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([]);
        _errorsDescriptor.PermissionsProviderNotFound(providerName).Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionName, providerName, "key", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    #endregion

    #region SetAsync - Multiple Permissions

    [Fact]
    public async Task should_set_multiple_at_once()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Update"];
        const string providerName = "Role";
        const string providerKey = "admin";

        var permissions = permissionNames.Select(n => _CreatePermission(n)).ToList();

        var provider = Substitute.For<IPermissionGrantProvider>();
        provider.Name.Returns(providerName);

        _definitionManager.GetPermissionsAsync(AbortToken).Returns(permissions);
        _grantProviderManager.ValueProviders.Returns([provider]);

        // when
        await _sut.SetAsync(permissionNames, providerName, providerKey, true, AbortToken);

        // then
        await provider
            .Received(1)
            .SetAsync(
                Arg.Is<IReadOnlyCollection<PermissionDefinition>>(p => p.Count == 2),
                providerKey,
                true,
                AbortToken
            );
    }

    [Fact]
    public async Task should_validate_all_defined_in_batch()
    {
        // given
        string[] permissionNames = ["Users.Create", "NonExistent.Permission"];
        var permission = _CreatePermission("Users.Create");
        var errorDescriptor = new ErrorDescriptor("test", "Some permissions undefined");

        _definitionManager.GetPermissionsAsync(AbortToken).Returns([permission]);
        _errorsDescriptor
            .SomePermissionsAreNotDefined(Arg.Any<IReadOnlyCollection<string>>())
            .Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionNames, "Role", "admin", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_validate_all_enabled_in_batch()
    {
        // given
        string[] permissionNames = ["Users.Create", "Users.Delete"];
        var enabledPermission = _CreatePermission("Users.Create");
        var disabledPermission = _CreatePermission("Users.Delete", isEnabled: false);
        var errorDescriptor = new ErrorDescriptor("test", "Some permissions disabled");

        _definitionManager.GetPermissionsAsync(AbortToken).Returns([enabledPermission, disabledPermission]);
        _errorsDescriptor
            .SomePermissionsAreDisabled(Arg.Any<IReadOnlyCollection<string>>())
            .Returns(ValueTask.FromResult(errorDescriptor));

        // when
        var act = () => _sut.SetAsync(permissionNames, "Role", "admin", true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task should_delete_all_grants()
    {
        // given
        const string providerName = "Role";
        const string providerKey = "admin";
        var grants = new List<PermissionGrantRecord>
        {
            new(Guid.NewGuid(), "Users.Create", providerName, providerKey, true),
            new(Guid.NewGuid(), "Users.Delete", providerName, providerKey, true),
        };

        _repository.GetListAsync(providerName, providerKey, AbortToken).Returns(grants);

        // when
        await _sut.DeleteAsync(providerName, providerKey, AbortToken);

        // then
        await _repository.Received(1).DeleteManyAsync(grants, AbortToken);
    }

    #endregion

    #region Helpers

    private static PermissionDefinition _CreatePermission(string name, bool isEnabled = true)
    {
        // PermissionDefinition has internal constructor, so use reflection
        var ctor = typeof(PermissionDefinition).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(string), typeof(bool)],
            null
        );

        return (PermissionDefinition)ctor!.Invoke([name, name, isEnabled]);
    }

    #endregion
}
