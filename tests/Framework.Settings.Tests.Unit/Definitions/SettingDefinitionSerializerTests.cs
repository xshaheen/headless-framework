// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Settings.Definitions;
using Framework.Settings.Entities;
using Framework.Settings.Models;
using Framework.Testing.Tests;

namespace Tests.Definitions;

public sealed class SettingDefinitionSerializerTests : TestBase
{
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();
    private readonly SettingDefinitionSerializer _sut;
    private readonly Guid _generatedId = Guid.NewGuid();

    public SettingDefinitionSerializerTests()
    {
        _guidGenerator.Create().Returns(_generatedId);
        _sut = new SettingDefinitionSerializer(_guidGenerator);
    }

    [Fact]
    public void should_serialize_definition_to_record()
    {
        // given
        var definition = new SettingDefinition(
            name: "TestSetting",
            defaultValue: "default-value",
            displayName: "Test Setting",
            description: "A test setting description",
            isVisibleToClients: true,
            isInherited: false,
            isEncrypted: true
        );

        // when
        var record = _sut.Serialize(definition);

        // then
        record.Id.Should().Be(_generatedId);
        record.Name.Should().Be("TestSetting");
        record.DisplayName.Should().Be("Test Setting");
        record.Description.Should().Be("A test setting description");
        record.DefaultValue.Should().Be("default-value");
        record.IsVisibleToClients.Should().BeTrue();
        record.IsInherited.Should().BeFalse();
        record.IsEncrypted.Should().BeTrue();
    }

    [Fact]
    public void should_deserialize_record_to_definition()
    {
        // given
        var record = new SettingDefinitionRecord(
            id: Guid.NewGuid(),
            name: "TestSetting",
            displayName: "Test Setting",
            description: "A test setting description",
            defaultValue: "default-value",
            providers: "Provider1,Provider2",
            isVisibleToClients: true,
            isInherited: false,
            isEncrypted: true
        );

        // when
        var definition = _sut.Deserialize(record);

        // then
        definition.Name.Should().Be("TestSetting");
        definition.DisplayName.Should().Be("Test Setting");
        definition.Description.Should().Be("A test setting description");
        definition.DefaultValue.Should().Be("default-value");
        definition.IsVisibleToClients.Should().BeTrue();
        definition.IsInherited.Should().BeFalse();
        definition.IsEncrypted.Should().BeTrue();
        definition.Providers.Should().BeEquivalentTo(["Provider1", "Provider2"]);
    }

    [Fact]
    public void should_preserve_all_properties()
    {
        // given
        var original = new SettingDefinition(
            name: "RoundTripSetting",
            defaultValue: "round-trip-value",
            displayName: "Round Trip Setting",
            description: "Round trip description",
            isVisibleToClients: true,
            isInherited: true,
            isEncrypted: false
        );
        original.Providers.AddRange(["Provider1", "Provider2", "Provider3"]);
        original["CustomKey"] = "CustomValue";
        original["IntKey"] = 42;

        // when
        var record = _sut.Serialize(original);
        var deserialized = _sut.Deserialize(record);

        // then
        deserialized.Name.Should().Be(original.Name);
        deserialized.DisplayName.Should().Be(original.DisplayName);
        deserialized.Description.Should().Be(original.Description);
        deserialized.DefaultValue.Should().Be(original.DefaultValue);
        deserialized.IsVisibleToClients.Should().Be(original.IsVisibleToClients);
        deserialized.IsInherited.Should().Be(original.IsInherited);
        deserialized.IsEncrypted.Should().Be(original.IsEncrypted);
        deserialized.Providers.Should().BeEquivalentTo(original.Providers);
        deserialized["CustomKey"].Should().Be("CustomValue");
        deserialized["IntKey"].Should().Be(42);
    }

    [Fact]
    public void should_handle_null_optional_properties()
    {
        // given
        var definition = new SettingDefinition(
            name: "MinimalSetting",
            defaultValue: null,
            displayName: null,
            description: null
        );

        // when
        var record = _sut.Serialize(definition);
        var deserialized = _sut.Deserialize(record);

        // then
        record.DefaultValue.Should().BeNull();
        record.Description.Should().BeNull();
        record.Providers.Should().BeNull();
        deserialized.DefaultValue.Should().BeNull();
        deserialized.Description.Should().BeNull();
        deserialized.Providers.Should().BeEmpty();
    }

    [Fact]
    public void should_serialize_providers_list()
    {
        // given
        var definition = new SettingDefinition("ProvidersSetting");
        definition.Providers.AddRange(["Global", "Tenant", "User"]);

        // when
        var record = _sut.Serialize(definition);

        // then
        record.Providers.Should().Be("Global,Tenant,User");
    }

    [Fact]
    public void should_serialize_custom_properties()
    {
        // given
        var definition = new SettingDefinition("CustomPropsSetting");
        definition["StringProp"] = "StringValue";
        definition["IntProp"] = 123;
        definition["BoolProp"] = true;
        definition["NullProp"] = null;

        // when
        var record = _sut.Serialize(definition);

        // then
        record.ExtraProperties.Should().HaveCount(4);
        record.ExtraProperties["StringProp"].Should().Be("StringValue");
        record.ExtraProperties["IntProp"].Should().Be(123);
        record.ExtraProperties["BoolProp"].Should().Be(true);
        record.ExtraProperties["NullProp"].Should().BeNull();
    }
}
