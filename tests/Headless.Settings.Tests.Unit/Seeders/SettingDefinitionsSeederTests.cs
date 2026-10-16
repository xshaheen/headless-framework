// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    private SettingDefinitionsSeeder _CreateSut(bool saveStatic) =>
        new(
            _services,
            Options.Create(new SettingManagementOptions { SaveStaticSettingsToDatabase = saveStatic }),
            NullLogger<SettingDefinitionsSeeder>.Instance,
            TimeProvider.System
        );
}
