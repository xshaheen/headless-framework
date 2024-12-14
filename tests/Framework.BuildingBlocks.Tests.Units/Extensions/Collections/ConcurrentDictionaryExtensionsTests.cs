// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Tests.Extensions.Collections;

public sealed class ConcurrentDictionaryExtensionsTests
{
    [Fact]
    public void get_or_default_should_return_default_if_key_does_not_exist()
    {
        // given
        var dictionary = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // when
        var result = dictionary.GetOrDefault("key");

        // then
        result.Should().Be(default);
    }

    [Fact]
    public void try_update_should_update_value_if_key_exists()
    {
        // given
        var dictionary = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        dictionary.TryAdd("key", 42);

        // when
        var updated = dictionary.TryUpdate("key", (key, oldValue) => oldValue + 1);
        var value = dictionary["key"];

        // then
        updated.Should().BeTrue();
        value.Should().Be(43);
    }

    [Fact]
    public void try_update_should_return_false_if_key_does_not_exist()
    {
        // given
        var dictionary = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // when
        var updated = dictionary.TryUpdate("key", (key, oldValue) => oldValue + 1);

        // then
        updated.Should().BeFalse();
    }
}
