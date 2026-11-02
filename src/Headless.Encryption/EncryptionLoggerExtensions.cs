// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Encryption;

internal static partial class EncryptionLoggerExtensions
{
    [LoggerMessage(
        EventId = 4300,
        EventName = "DataKeyCreated",
        Level = LogLevel.Information,
        Message = "Created {Kind} data key {KeyId} version {Version} for scope {Scope}"
    )]
    internal static partial void LogDataKeyCreated(
        this ILogger logger,
        DataKeyKind kind,
        Guid keyId,
        int version,
        string scope
    );

    [LoggerMessage(
        EventId = 4301,
        EventName = "DataKeysShredded",
        Level = LogLevel.Warning,
        Message = "Shredded scope {Scope}: deleted {Count} data keys"
    )]
    internal static partial void LogDataKeysShredded(this ILogger logger, string scope, int count);

    [LoggerMessage(
        EventId = 4302,
        EventName = "DataKeysRewrapped",
        Level = LogLevel.Information,
        Message = "Re-wrapped {Count} data keys under key-encryption key {KekId}"
    )]
    internal static partial void LogDataKeysRewrapped(this ILogger logger, int count, string? kekId);

    [LoggerMessage(
        EventId = 4303,
        EventName = "DataKeyRetired",
        Level = LogLevel.Information,
        Message = "Retired {Kind} data key {KeyId} version {Version} of scope {Scope}"
    )]
    internal static partial void LogDataKeyRetired(
        this ILogger logger,
        DataKeyKind kind,
        Guid keyId,
        int version,
        string scope
    );
}
