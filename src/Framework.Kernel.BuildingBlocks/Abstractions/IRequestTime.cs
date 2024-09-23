// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IRequestTime
{
    public DateTimeOffset Now { get; }
}

public sealed class RequestTime(IClock clock) : IRequestTime
{
    public DateTimeOffset Now { get; } = clock.UtcNow;
}
