// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.BuildingBlocks.Abstractions;

public interface IRequestTime
{
    public DateTimeOffset Now { get; }
}

public sealed class RequestTime(IClock clock) : IRequestTime
{
    public DateTimeOffset Now { get; } = clock.UtcNow;
}
