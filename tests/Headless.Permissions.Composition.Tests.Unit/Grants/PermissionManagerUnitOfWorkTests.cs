// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Permissions.Resources;
using Headless.Primitives;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests.Grants;

public sealed class PermissionManagerUnitOfWorkTests : TestBase
{
    private const string _ProviderName = "Role";
    private const string _ProviderKey = "admin";

    private readonly IPermissionDefinitionManager _definitionManager = Substitute.For<IPermissionDefinitionManager>();

    private readonly IPermissionGrantProviderManager _grantProviderManager =
        Substitute.For<IPermissionGrantProviderManager>();

    private readonly IPermissionGrantStore _grantStore = Substitute.For<IPermissionGrantStore>();
    private readonly IPermissionErrorsDescriptor _errorsDescriptor = Substitute.For<IPermissionErrorsDescriptor>();
    private readonly ICurrentTenant _currentTenant = Substitute.For<ICurrentTenant>();
    private readonly IBus _bus = Substitute.For<IBus>();
    private readonly IPermissionGrantProvider _provider = Substitute.For<IPermissionGrantProvider>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();
    private readonly List<Func<ValueTask>> _afterCommit = [];
    private readonly PermissionManager _sut;

    public PermissionManagerUnitOfWorkTests()
    {
        var hostIdentity = Substitute.For<IHostIdentityAccessor>();
        hostIdentity.HostName.Returns("prod/orders-7d");
        _provider.Name.Returns(_ProviderName);
        _grantProviderManager.ValueProviders.Returns([_provider]);
        _unit.OnCompleted(Arg.Do<Func<ValueTask>>(_afterCommit.Add)).Returns(Substitute.For<IDisposable>());

        _sut = new PermissionManager(
            _definitionManager,
            _grantProviderManager,
            _grantStore,
            _errorsDescriptor,
            hostIdentity,
            _currentTenant,
            _bus
        );
    }

    [Fact]
    public async Task should_write_through_the_provider_enlisted_path_and_announce_only_after_commit()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        _definitionManager.FindAsync("Users.Create", AbortToken).Returns(permission);

        // when
        await _sut.SetAsync(_unit, "Users.Create", _ProviderName, _ProviderKey, isGranted: true, AbortToken);

        // then the write enlisted and nothing was announced yet
        await _provider
            .Received(1)
            .SetEnlistedAsync(
                _unit,
                Arg.Is<IReadOnlyCollection<PermissionDefinition>>(x => x.Single() == permission),
                _ProviderKey,
                true,
                AbortToken
            );
        await _provider
            .DidNotReceiveWithAnyArgs()
            .SetAsync(default(PermissionDefinition)!, default!, default, AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(PermissionGrantChangedMessage), AbortToken);

        // and the announcement goes out when the unit commits
        await _CommitAsync();
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<PermissionGrantChangedMessage>(m =>
                    m.PermissionNames.Single() == "Users.Create"
                    && m.ProviderName == _ProviderName
                    && m.ProviderKey == _ProviderKey
                    && m.OriginHostName == "prod/orders-7d"
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task should_announce_a_batch_under_the_tenant_that_wrote_it()
    {
        // given
        string[] names = ["Users.Create", "Users.Update"];
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(names.Select(n => _CreatePermission(n)).ToList());
        _currentTenant.Id.Returns("tenant-a");
        _currentTenant.Name.Returns("Tenant A");

        // when the write ran in tenant-a, and the unit completes after the code left it
        await _sut.SetAsync(_unit, names, _ProviderName, _ProviderKey, isGranted: false, AbortToken);
        _currentTenant.Id.Returns((string?)null);
        await _CommitAsync();

        // then
        _currentTenant.Received(1).Change("tenant-a", "Tenant A");
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<PermissionGrantChangedMessage>(m => m.PermissionNames.Count == 2),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task should_delete_through_the_store_enlisted_path_and_announce_after_commit()
    {
        // given
        _grantStore
            .DeleteEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken)
            .Returns(["Users.Create", "Users.Delete"]);

        // when
        await _sut.DeleteAsync(_unit, _ProviderName, _ProviderKey, AbortToken);

        // then
        await _grantStore.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default!, AbortToken);
        await _bus.DidNotReceiveWithAnyArgs().PublishAsync(default(PermissionGrantChangedMessage), AbortToken);
        await _CommitAsync();
        await _bus.Received(1)
            .PublishAsync(
                Arg.Is<PermissionGrantChangedMessage>(m => m.PermissionNames.Count == 2),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task should_register_no_announcement_when_an_enlisted_delete_removed_nothing()
    {
        // given
        _grantStore.DeleteEnlistedAsync(_unit, _ProviderName, _ProviderKey, AbortToken).Returns([]);

        // when
        await _sut.DeleteAsync(_unit, _ProviderName, _ProviderKey, AbortToken);

        // then
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_an_undefined_permission_before_writing_or_registering_work()
    {
        // given
        _definitionManager.FindAsync("Missing", AbortToken).Returns((PermissionDefinition?)null);
        _errorsDescriptor.PermissionIsNotDefined("Missing").Returns(new ErrorDescriptor("g:missing", "missing"));

        // when
        var act = () => _sut.SetAsync(_unit, "Missing", _ProviderName, _ProviderKey, isGranted: true, AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
        _provider
            .ReceivedCalls()
            .Should()
            .NotContain(c => !string.Equals(c.GetMethodInfo().Name, "get_Name", StringComparison.Ordinal));
        _afterCommit.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_null_unit()
    {
        // when
        var act = () => _sut.SetAsync(null!, "Users.Create", _ProviderName, _ProviderKey, isGranted: true, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task should_refuse_a_provider_that_cannot_enlist()
    {
        // given a provider that implements only the autonomous members
        var permission = _CreatePermission("Users.Create");
        _definitionManager.FindAsync("Users.Create", AbortToken).Returns(permission);
        _grantProviderManager.ValueProviders.Returns([new AutonomousOnlyProvider()]);

        // when
        var act = () => _sut.SetAsync(_unit, "Users.Create", "Custom", _ProviderKey, isGranted: true, AbortToken);

        // then
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*'Custom'*");
        _afterCommit.Should().BeEmpty();
    }

    private async Task _CommitAsync()
    {
        foreach (var work in _afterCommit)
        {
            await work();
        }
    }

    private static PermissionDefinition _CreatePermission(string name)
    {
        var ctor = typeof(PermissionDefinition).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(string), typeof(bool)],
            null
        );

        return (PermissionDefinition)ctor!.Invoke([name, name, true]);
    }

    private sealed class AutonomousOnlyProvider : IPermissionGrantProvider
    {
        public string Name => "Custom";

        public Task<PermissionGrantResult> CheckAsync(
            PermissionDefinition permission,
            ICurrentUser currentUser,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<MultiplePermissionGrantStatusResult> CheckAsync(
            IReadOnlyCollection<PermissionDefinition> permissions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetAsync(
            PermissionDefinition permission,
            string providerKey,
            bool isGranted,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task SetAsync(
            IReadOnlyCollection<PermissionDefinition> permissions,
            string providerKey,
            bool isGranted,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
