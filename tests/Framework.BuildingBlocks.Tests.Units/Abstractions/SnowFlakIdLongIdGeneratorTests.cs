// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;

namespace Tests.Abstractions;

public sealed class SnowFlakIdLongIdGeneratorTests
{
    [Fact]
    public void create_should_generate_unique_ids()
    {
        // given
        var generator = new SnowFlakIdLongIdGenerator(generatorId: 1);

        // when
        var ids = new[]
        {
            generator.Create(),
            generator.Create(),
            generator.Create(),
            generator.Create(),
            generator.Create(),
        };

        // Sort the IDs
        var orderedIds = ids.Order().ToArray();

        // Compare each ID to ensure the original and ordered lists match
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i].Should().Be(orderedIds[i], $"ID at index {i} should match in both original and ordered arrays");
        }

        // then
        ids.Should().OnlyHaveUniqueItems("each generated ID must be unique");
    }
}
