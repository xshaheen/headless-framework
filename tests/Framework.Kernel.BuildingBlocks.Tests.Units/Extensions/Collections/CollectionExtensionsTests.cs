// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Tests.Extensions.Collections;

public sealed class CollectionExtensionsTests
{
    [Fact]
    public void AddIfNotContains_with_predicate()
    {
        List<int> collection = [4, 5, 6];

        collection.AddIfNotContains(x => x == 5, () => 5);
        collection.Should().HaveCount(3);

        collection.AddIfNotContains(x => x == 42, () => 42);
        collection.Should().HaveCount(4);

        collection.AddIfNotContains(x => x < 8, () => 8);
        collection.Should().HaveCount(4);

        collection.AddIfNotContains(x => x > 999, () => 8);
        collection.Should().HaveCount(5);
    }
}
