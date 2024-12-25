// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Framework.BuildingBlocks.Abstractions;

public interface ILongIdGenerator
{
    /// <summary>Creates a new <see cref="long"/>.</summary>
    long Create();
}

public sealed class SnowFlakIdLongIdGenerator(short generatorId = 0) : ILongIdGenerator
{
    private readonly SnowflakeId _generator = new(generatorId);

    public long Create() => _generator.NewId();
}
