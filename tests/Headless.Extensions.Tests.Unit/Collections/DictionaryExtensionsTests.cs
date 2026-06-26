// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;

namespace Tests.Collections;

public sealed class DictionaryExtensionsTests
{
    [Fact]
    public void dictionary_equal_should_return_true_when_dictionaries_are_equal()
    {
        // given
        var first = new Dictionary<string, int>(StringComparer.Ordinal) { { "one", 1 }, { "two", 2 } };

        var second = new Dictionary<string, int>(StringComparer.Ordinal) { { "one", 1 }, { "two", 2 } };

        // when
        var result = first.DictionaryEqual(second);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void dictionary_equal_should_return_false_when_dictionaries_have_different_values()
    {
        // given
        var first = new Dictionary<string, int>(StringComparer.Ordinal) { { "one", 1 }, { "two", 2 } };

        var second = new Dictionary<string, int>(StringComparer.Ordinal) { { "one", 1 }, { "two", 3 } };

        // when
        var result = first.DictionaryEqual(second);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void get_or_default_should_return_default_value_if_key_does_not_exist()
    {
        // given
        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

        // when
        var result = dictionary.GetOrDefault("key");

        // then
        result.Should().Be(0);
    }

    [Fact]
    public void get_or_add_should_return_existing_value_if_key_exists()
    {
        // given
        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal) { { "key", 42 } };

        // when
        var result = dictionary.GetOrAdd("key", _ => 100);

        // then
        result.Should().Be(42);
    }

    [Fact]
    public void get_or_add_should_add_and_return_value_if_key_does_not_exist()
    {
        // given
        var dictionary = new Dictionary<string, int>(StringComparer.Ordinal);

        // when
        var result = dictionary.GetOrAdd("key", _ => 100);

        // then
        result.Should().Be(100);
        dictionary.Should().ContainKey("key").WhoseValue.Should().Be(100);
    }

    [Fact]
    public void add_dictionary_should_copy_all_pairs_and_return_same_instance()
    {
        // given
        var target = new Dictionary<string, int>(StringComparer.Ordinal) { { "a", 1 } };
        var other = new Dictionary<string, int>(StringComparer.Ordinal) { { "b", 2 }, { "c", 3 } };

        // when
        var result = target.AddDictionary(other);

        // then
        result.Should().BeSameAs(target);
        target.Should().HaveCount(3);
        target.Should().ContainKey("b").WhoseValue.Should().Be(2);
        target.Should().ContainKey("c").WhoseValue.Should().Be(3);
    }

    [Fact]
    public void add_dictionary_should_copy_all_pairs_from_readonly_dictionary()
    {
        // given
        var target = new Dictionary<string, int>(StringComparer.Ordinal) { { "a", 1 } };
        var other = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.Ordinal) { { "b", 2 } }
        );

        // when
        var result = target.AddDictionary(other);

        // then
        result.Should().BeSameAs(target);
        target.Should().HaveCount(2);
        target.Should().ContainKey("b").WhoseValue.Should().Be(2);
    }
}
