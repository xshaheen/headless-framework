// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IHaveTimeProvider
{
    TimeProvider TimeProvider { get; }
}

public static class TimeProviderExtensions
{
    public static TimeProvider GetTimeProvider(this object target)
    {
        return target is IHaveTimeProvider accessor ? accessor.TimeProvider : TimeProvider.System;
    }
}
