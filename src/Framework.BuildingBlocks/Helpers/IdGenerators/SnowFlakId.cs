using IdGen;

namespace Framework.BuildingBlocks.Helpers.IdGenerators;

// Ref: https://github.com/RobThree/IdGen
// https://github.com/RobThree/IdGen/issues/34
// https://www.callicoder.com/distributed-unique-id-sequence-number-generator/
[PublicAPI]
public static class SnowFlakId
{
    static SnowFlakId() => Configure(1080);

    private static IdGenerator? _generator;

    public static void Configure(int generatorId)
    {
        Argument.IsPositive(generatorId);

        // Let's say we take jan 17st 2022 as our epoch
        var epoch = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Create an ID with 45 bits for timestamp, 2 for generator-id
        // and 16 for sequence
        var structure = new IdStructure(45, 2, 16);

        // Prepare options
        var options = new IdGeneratorOptions(structure, new DefaultTimeSource(epoch));

        // Create an IdGenerator with it's generator-id set to 0, our custom epoch
        // and id-structure
        _generator = new IdGenerator(0, options);
    }

    public static long NewId() =>
        _generator?.CreateId() ?? throw new InvalidOperationException("IdGenerator is not configured");
}
