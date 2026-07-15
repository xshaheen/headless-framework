// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Tests.Collections;

public sealed class ConcurrentDictionaryExtensionsTests
{
    [Fact]
    public void should_return_default_if_key_does_not_exist_when_get_or_default()
    {
        // given
        var dictionary = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        // when
        var result = dictionary.GetOrDefault("key");

        // then
        result.Should().Be(0);
    }
}
