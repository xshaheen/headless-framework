// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Registration;

namespace Tests.Internal;

public sealed class MessageMetadataRegistryTests
{
    [Fact]
    public void should_lookup_registered_message_metadata()
    {
        // given
        var config = new FakeProviderConfig("value");
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(TestMessage), null, null, _Configs(config), []),
        ]);

        // when
        var found = registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        found.Should().BeTrue();
        metadata!.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(config);
    }

    [Fact]
    public void should_return_false_for_unregistered_message_type()
    {
        // given
        var registry = new MessageMetadataRegistry([]);

        // when
        var found = registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        found.Should().BeFalse();
        metadata.Should().BeNull();
    }

    [Fact]
    public void should_resolve_assignable_metadata_for_concrete_message()
    {
        // given
        var config = new FakeProviderConfig("interface");
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(ITestEvent), null, null, _Configs(config), []),
        ]);

        // when
        var found = registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        found.Should().BeTrue();
        metadata!.MessageType.Should().Be<ITestEvent>();
    }

    [Fact]
    public void should_throw_when_assignable_metadata_resolution_is_ambiguous()
    {
        // given
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(ITestEvent), null, null, _Configs(new FakeProviderConfig("a")), []),
            new MessageRegistration(typeof(IOtherEvent), null, null, _Configs(new OtherProviderConfig("b")), []),
        ]);

        // when
        var act = () => registry.TryGet(typeof(TestMessage), out _);

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*multiple registered metadata types*IOtherEvent*ITestEvent*");
    }

    [Fact]
    public void should_merge_repeated_registrations_for_same_type()
    {
        // given
        var first = new FakeProviderConfig("a");
        var second = new OtherProviderConfig("b");
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(TestMessage), null, null, _Configs(first), []),
            new MessageRegistration(typeof(TestMessage), null, null, _Configs(second), []),
        ]);

        // when
        registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        metadata!.ProviderConfigs.Values.Should().BeEquivalentTo<object>([first, second]);
    }

    [Fact]
    public void should_use_last_correlation_selector_when_message_type_is_registered_more_than_once()
    {
        // given
        string? First(object _) => "first";
        string? Second(object _) => "second";
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(TestMessage), null, First, new Dictionary<Type, object>(), []),
            new MessageRegistration(typeof(TestMessage), null, Second, new Dictionary<Type, object>(), []),
        ]);

        // when
        registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        metadata!.CorrelationSelector!(new TestMessage()).Should().Be("second");
    }

    [Fact]
    public void should_use_last_provider_config_when_message_type_is_registered_more_than_once()
    {
        // given
        var first = new FakeProviderConfig("a");
        var second = new FakeProviderConfig("b");
        var registry = new MessageMetadataRegistry([
            new MessageRegistration(typeof(TestMessage), null, null, _Configs(first), []),
            new MessageRegistration(typeof(TestMessage), null, null, _Configs(second), []),
        ]);

        // when
        registry.TryGet(typeof(TestMessage), out var metadata);

        // then
        metadata!.ProviderConfigs[typeof(FakeProviderConfig)].Should().Be(second);
    }

    private static IReadOnlyDictionary<Type, object> _Configs(object config) =>
        new Dictionary<Type, object> { [config.GetType()] = config };

    private interface ITestEvent;

    private interface IOtherEvent;

    private sealed record TestMessage : ITestEvent, IOtherEvent;

    private sealed record FakeProviderConfig(string Value);

    private sealed record OtherProviderConfig(string Value);
}
