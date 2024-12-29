// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Abstractions;

public interface IHaveLogger
{
    ILogger Logger { get; }
}

[PublicAPI]
public static class LoggerExtensions
{
    public static ILogger GetLogger(this object target)
    {
        return target is IHaveLogger accessor ? accessor.Logger : NullLogger.Instance;
    }
}
