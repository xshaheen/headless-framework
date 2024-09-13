// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IHaveLogger
{
    ILogger Logger { get; }
}

public static class LoggerExtensions
{
    public static ILogger GetLogger(this object target)
    {
        return target is IHaveLogger accessor ? accessor.Logger : NullLogger.Instance;
    }
}
