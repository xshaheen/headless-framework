// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Context;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Services;

/// <summary>The permission definitions seeder: its name, its option gate, and the store it saves; the retry is the shared base's.</summary>
public sealed class PermissionDefinitionsSeederTests : TestBase
{
    private readonly IDynamicPermissionDefinitionStore _store = Substitute.For<IDynamicPermissionDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public PermissionDefinitionsSeederTests()
    {
        _services.GetService(typeof(IDynamicPermissionDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_permission_definitions()
    {
        ISeeder sut = _CreateSut(saveStatic: true);

        sut.Name.Should().Be("PermissionDefinitions");
    }

    [Fact]
    public async Task should_not_touch_the_store_when_saving_static_definitions_is_disabled()
    {
        var sut = _CreateSut(saveStatic: false);

        await sut.SeedAsync(AbortToken);

        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_save_the_static_definitions_when_enabled()
    {
        var sut = _CreateSut(saveStatic: true);

        await sut.SeedAsync(AbortToken);

        await _store.Received(1).SaveAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_current_without_writing_or_locking_when_the_store_holds_every_definition()
    {
        // given: the stored records carry other ids, which the comparison ignores
        var store = new VerifiableStore();
        store.Declare([_Group("Users")], [_Permission("Users", "Users.Create")]);
        store.Store([_Group("Users")], [_Permission("Users", "Users.Create")]);

        // when
        var state = await store.Seeder.VerifyAsync(AbortToken);

        // then
        state.Should().Be(SeedState.Current);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_declared_group_is_not_stored()
    {
        var store = new VerifiableStore();
        store.Declare([_Group("Users")], []);
        store.Store([], []);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_stored_permission_differs_from_its_declaration()
    {
        var store = new VerifiableStore();
        store.Declare([_Group("Users")], [_Permission("Users", "Users.Create", "Create users")]);
        store.Store([_Group("Users")], [_Permission("Users", "Users.Create", "Add users")]);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
    }

    [Fact]
    public async Task should_report_stale_when_a_group_configured_as_deleted_is_still_stored()
    {
        var store = new VerifiableStore();
        store.Providers.DeletedPermissionGroups.Add("Legacy");
        store.Declare([_Group("Users")], []);
        store.Store([_Group("Users"), _Group("Legacy")], []);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
    }

    [Fact]
    public async Task should_report_unknown_when_the_application_replaced_the_store()
    {
        var sut = _CreateSut(saveStatic: true);

        var state = await sut.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Unknown);
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    private static PermissionGroupDefinitionRecord _Group(string name) => new(Guid.NewGuid(), name, name);

    private static PermissionDefinitionRecord _Permission(string group, string name, string? displayName = null) =>
        new(Guid.NewGuid(), group, name, parentName: null, displayName ?? name);

    /// <summary>A real dynamic store over substituted records, so the seeder's comparison runs as it does in a host.</summary>
    private sealed class VerifiableStore
    {
        private readonly IPermissionDefinitionRecordRepository _repository =
            Substitute.For<IPermissionDefinitionRecordRepository>();
        private readonly IPermissionDefinitionSerializer _serializer =
            Substitute.For<IPermissionDefinitionSerializer>();
        private readonly ICache _cache = Substitute.For<ICache>();
        private readonly IDistributedLock _lock = Substitute.For<IDistributedLock>();
        private readonly IBus _bus = Substitute.For<IBus>();

        public VerifiableStore()
        {
            var application = Substitute.For<IHostIdentityAccessor>();
            application.ApplicationName.Returns("TestApp");
            var store = new DynamicPermissionDefinitionStore(
                _repository,
                Substitute.For<IStaticPermissionDefinitionStore>(),
                _serializer,
                _cache,
                _lock,
                Substitute.For<IGuidGenerator>(),
                application,
                Options.Create(new PermissionManagementOptions()),
                Options.Create(Providers),
                TimeProvider.System,
                _bus
            );
            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IDynamicPermissionDefinitionStore)).Returns(store);
            Seeder = new PermissionDefinitionsSeeder(
                services,
                Options.Create(new PermissionManagementOptions { SaveStaticPermissionsToDatabase = true }),
                NullLogger<PermissionDefinitionsSeeder>.Instance,
                TimeProvider.System
            );
        }

        public PermissionManagementProvidersOptions Providers { get; } = new();

        public PermissionDefinitionsSeeder Seeder { get; }

        public void Declare(
            List<PermissionGroupDefinitionRecord> groups,
            List<PermissionDefinitionRecord> permissions
        ) => _serializer.Serialize(Arg.Any<IEnumerable<PermissionGroupDefinition>>()).Returns((groups, permissions));

        public void Store(List<PermissionGroupDefinitionRecord> groups, List<PermissionDefinitionRecord> permissions)
        {
            _repository.GetGroupsListAsync(Arg.Any<CancellationToken>()).Returns(groups);
            _repository.GetPermissionsListAsync(Arg.Any<CancellationToken>()).Returns(permissions);
        }

        public void AssertNothingWrittenOrLocked()
        {
            _repository
                .ReceivedCalls()
                .Should()
                .OnlyContain(c => c.GetMethodInfo().Name.StartsWith("Get", StringComparison.Ordinal));
            _lock.ReceivedCalls().Should().BeEmpty();
            _cache.ReceivedCalls().Should().BeEmpty();
            _bus.ReceivedCalls().Should().BeEmpty();
        }
    }

    private PermissionDefinitionsSeeder _CreateSut(bool saveStatic) =>
        new(
            _services,
            Options.Create(new PermissionManagementOptions { SaveStaticPermissionsToDatabase = saveStatic }),
            NullLogger<PermissionDefinitionsSeeder>.Instance,
            TimeProvider.System
        );
}
