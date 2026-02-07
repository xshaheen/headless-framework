// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Exceptions;
using Headless.Settings.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Helpers;

public interface ISettingEncryptionService
{
    string? Encrypt(SettingDefinition settingDefinition, string? plainValue);

    string? Decrypt(SettingDefinition settingDefinition, string? encryptedValue);
}

public sealed class SettingEncryptionService(
    IStringEncryptionService stringEncryptionService,
    ILogger<SettingEncryptionService> logger
) : ISettingEncryptionService
{
    public string? Encrypt(SettingDefinition settingDefinition, string? plainValue)
    {
        if (plainValue.IsNullOrEmpty())
        {
            return plainValue;
        }

        try
        {
            return stringEncryptionService.Encrypt(plainValue);
        }
        catch (Exception e)
        {
            logger.LogFailedToEncryptSettingValue(e, settingDefinition.Name);

            throw;
        }
    }

    public string? Decrypt(SettingDefinition settingDefinition, string? encryptedValue)
    {
        if (encryptedValue.IsNullOrEmpty())
        {
            return encryptedValue;
        }

        try
        {
            return stringEncryptionService.Decrypt(encryptedValue);
        }
        catch (Exception e)
        {
            logger.LogFailedToDecryptSettingValue(e, settingDefinition.Name);

            throw new ConflictException($@"Failed to decrypt setting '{settingDefinition.Name}'.", e);
        }
    }
}

internal static partial class SettingEncryptionServiceLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToEncryptSettingValue",
        Level = LogLevel.Warning,
        Message = "Failed to encrypt setting value: {SettingDefinition}"
    )]
    public static partial void LogFailedToEncryptSettingValue(
        this ILogger logger,
        Exception exception,
        string settingDefinition
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToDecryptSettingValue",
        Level = LogLevel.Error,
        Message = "Failed to decrypt setting value: {SettingDefinition}"
    )]
    public static partial void LogFailedToDecryptSettingValue(
        this ILogger logger,
        Exception exception,
        string settingDefinition
    );
}
