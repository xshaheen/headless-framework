// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Hosting.Seeders;

internal static partial class SeederLoggerExtensions
{
    [LoggerMessage(EventId = 1, EventName = "PreSeeding", Level = LogLevel.Information, Message = ">>> Pre-Seeding")]
    public static partial void LogPreSeeding(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "PreSeedingUsing",
        Level = LogLevel.Information,
        Message = ">>> Pre-Seeding using {TypeName}"
    )]
    public static partial void LogPreSeedingUsing(this ILogger logger, string typeName);

    [LoggerMessage(
        EventId = 3,
        EventName = "PreSeedingCompleted",
        Level = LogLevel.Information,
        Message = ">>> Pre-Seeding completed"
    )]
    public static partial void LogPreSeedingCompleted(this ILogger logger);

    [LoggerMessage(EventId = 4, EventName = "Seeding", Level = LogLevel.Information, Message = ">>> Seeding")]
    public static partial void LogSeeding(this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        EventName = "SeedingUsing",
        Level = LogLevel.Information,
        Message = ">>> Seeding using {TypeName}"
    )]
    public static partial void LogSeedingUsing(this ILogger logger, string typeName);

    [LoggerMessage(
        EventId = 6,
        EventName = "SeedingCompleted",
        Level = LogLevel.Information,
        Message = ">>> Seeding completed"
    )]
    public static partial void LogSeedingCompleted(this ILogger logger);
}
