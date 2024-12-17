// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using IdGen;

namespace Framework.BuildingBlocks.Abstractions;

public interface IUniqueLongGenerator
{
    /// <summary>Creates a new <see cref="long"/>.</summary>
    long Create();
}

public sealed class SnowFlakIdUniqueLongGenerator : IUniqueLongGenerator
{
    private readonly IdGenerator _generator;

    public SnowFlakIdUniqueLongGenerator(int generatorId = 0)
    {
        Argument.IsPositiveOrZero(generatorId);

        // Create an ID with 45 bits for timestamp, 2 for generator-id and 16 for sequence
        var structure = new IdStructure(timestampBits: 45, generatorIdBits: 2, sequenceBits: 16);

        // Let's say we take jan 1st 2023 as our epoch
        var epoch = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Prepare options
        var options = new IdGeneratorOptions(structure, new DefaultTimeSource(epoch));

        // Create an IdGenerator with it's generator-id set to 0, our custom epoch and id-structure
        _generator = new IdGenerator(generatorId, options);
    }

    public long GeneratorId => _generator.Id;

    public long Create() => _generator.CreateId();
}
