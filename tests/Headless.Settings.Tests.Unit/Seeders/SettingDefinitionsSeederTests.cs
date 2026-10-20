// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Caching;
using Headless.Context;
using Headless.DistributedLocks;
using Headless.Hosting;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Seeders;

/// <summary>The setting definitions seeder: its name, its option gate, and the store it saves; the retry is the shared base's.</summary>
public sealed class SettingDefinitionsSeederTests : TestBase
{
    private readonly IDynamicSettingDefinitionStore _store = Substitute.For<IDynamicSettingDefinitionStore>();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();

    public SettingDefinitionsSeederTests()
    {
        _services.GetService(typeof(IDynamicSettingDefinitionStore)).Returns(_store);
    }

    [Fact]
    public void should_be_named_setting_definitions()
    {
        ISeeder sut = _CreateSut(saveStatic: true);

        sut.Name.Should().Be("SettingDefinitions");
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
        store.Declare([_Setting("Smtp.Host")]);
        store.Store([_Setting("Smtp.Host")]);

        // when
        var state = await store.Seeder.VerifyAsync(AbortToken);

        // then
        state.Should().Be(SeedState.Current);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_declared_setting_is_not_stored()
    {
        var store = new VerifiableStore();
        store.Declare([_Setting("Smtp.Host")]);
        store.Store([]);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
        store.AssertNothingWrittenOrLocked();
    }

    [Fact]
    public async Task should_report_stale_when_a_stored_setting_differs_from_its_declaration()
    {
        var store = new VerifiableStore();
        store.Declare([_Setting("Smtp.Host", defaultValue: "smtp.example.com")]);
        store.Store([_Setting("Smtp.Host", defaultValue: "localhost")]);

        var state = await store.Seeder.VerifyAsync(AbortToken);

        state.Should().Be(SeedState.Stale);
    }

    [Fact]
    public async Task should_report_stale_when_a_setting_configured_as_deleted_is_still_stored()
    {
        var store = new VerifiableStore();
        store.Providers.DeletedSettings.Add("Smtp.Legacy");
        store.Declare([_Setting("Smtp.Host")]);
        store.Store([_Setting("Smtp.Host"), _Setting("Smtp.Legacy")]);

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

    private static SettingDefinitionRecord _Setting(string name, string? defaultValue = null) =>
        new(
            Guid.NewGuid(),
            name,
            displayName: name,
            description: null,
            defaultValue,
            providers: null,
            isVisibleToClients: false,
            isInherited: true,
            isEncrypted: false
        );

    /// <summary>A real dynamic store over substituted records, so the seeder's comparison runs as it does in a host.</summary>
    private sealed class VerifiableStore
    {
        private readonly ISettingDefinitionRecordRepository _repository =
            Substitute.For<ISettingDefinitionRecordRepository>();
        private readonly ISettingDefinitionSerializer _serializer = Substitute.For<ISettingDefinitionSerializer>();
        private readonly ICache _cache = Substitute.For<ICache>();
        private readonly IDistributedLock _lock = Substitute.For<IDistributedLock>();

        public VerifiableStore()
        {
            var application = Substitute.For<IHostIdentityAccessor>();
            application.ApplicationName.Returns("TestApp");
            var store = new DynamicSettingDefinitionStore(
                _repository,
                Substitute.For<IStaticSettingDefinitionStore>(),
                _serializer,
                _cache,
                _lock,
                Substitute.For<IGuidGenerator>(),
                application,
                Options.Create(new SettingManagementOptions()),
                Options.Create(Providers),
                TimeProvider.System
            );
            var services = Substitute.For<IServiceProvider>();
            services.GetService(typeof(IDynamicSettingDefinitionStore)).Returns(store);
            Seeder = new SettingDefinitionsSeeder(
                services,
                Options.Create(new SettingManagementOptions { SaveStaticSettingsToDatabase = true }),
                NullLogger<SettingDefinitionsSeeder>.Instance,
                TimeProvider.System
            );
        }

        public SettingManagementProvidersOptions Providers { get; } = new();

        public SettingDefinitionsSeeder Seeder { get; }

        public void Declare(List<SettingDefinitionRecord> settings) =>
            _serializer.Serialize(Arg.Any<IEnumerable<SettingDefinition>>()).Returns(settings);

        public void Store(List<SettingDefinitionRecord> settings) =>
            _repository.GetListAsync(Arg.Any<CancellationToken>()).Returns(settings);

        public void AssertNothingWrittenOrLocked()
        {
            _repository
                .ReceivedCalls()
                .Should()
                .OnlyContain(c => c.GetMethodInfo().Name.StartsWith("Get", StringComparison.Ordinal));
            _lock.ReceivedCalls().Should().BeEmpty();
            _cache.ReceivedCalls().Should().BeEmpty();
        }
    }

    private SettingDefinitionsSeeder _CreateSut(bool saveStatic) =>
        new(
            _services,
            Options.Create(new SettingManagementOptions { SaveStaticSettingsToDatabase = saveStatic }),
            NullLogger<SettingDefinitionsSeeder>.Instance,
            TimeProvider.System
        );
}
