// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using IdGen;

namespace Framework.Core;

// Ref: https://github.com/RobThree/IdGen
// https://github.com/RobThree/IdGen/issues/34
// https://www.callicoder.com/distributed-unique-id-sequence-number-generator
[PublicAPI]
public sealed class SnowflakeId
{
    public static SnowflakeId Default { get; } = new();

    private readonly IdGenerator _generator;

    /// <summary>If we have multiple instance of the application we should use unique generatorId per instance.</summary>
    public SnowflakeId(short generatorId = 0)
    {
        Argument.IsPositive(generatorId);
        Argument.IsLessThanOrEqualTo(generatorId, 1024);

        var options = new IdGeneratorOptions
        {
            // Create an ID with 41-bits for timestamp, 10 for generator-id, and 12 for sequence (Total must be 63bits)
            IdStructure = new IdStructure(timestampBits: 41, generatorIdBits: 10, sequenceBits: 12),
            // Let's say we take 1/1/2024 as our epoch
            TimeSource = new DefaultTimeSource(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        _generator = new IdGenerator(generatorId, options);
    }

    public  long NewId() => _generator.CreateId();
}
