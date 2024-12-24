// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using IdGen;

namespace Framework.BuildingBlocks.Ids;

// Ref: https://github.com/RobThree/IdGen
// https://github.com/RobThree/IdGen/issues/34
// https://www.callicoder.com/distributed-unique-id-sequence-number-generator/
[PublicAPI]
public static class SnowFlakId
{
    static SnowFlakId()
    {
        Configure(3);
    }

    private static IdGenerator? _generator;

    /// <summary>
    /// If we have multiple instance of the application we should yous unique generatorId per instance.
    /// </summary>
    /// <param name="generatorId"></param>
    public static void Configure(int generatorId = 0)
    {
        Argument.IsPositive(generatorId);

        // Let's say we take jan 17th 2022 as our epoch
        var epoch = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Create an ID with 45 bits for timestamp, 2 for generator-id
        // and 16 for sequence
        var structure = new IdStructure(45, 2, 16);

        // Prepare options
        var options = new IdGeneratorOptions(structure, new DefaultTimeSource(epoch));

        // Create an IdGenerator with it's generator-id set to 0, our custom epoch
        // and id-structure
        _generator = new IdGenerator(generatorId, options);
    }

    public static long NewId() =>
        _generator?.CreateId() ?? throw new InvalidOperationException("IdGenerator is not configured");
}
