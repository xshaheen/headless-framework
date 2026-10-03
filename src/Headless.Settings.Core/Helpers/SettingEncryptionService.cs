// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Exceptions;
using Headless.Security;
using Headless.Settings.Models;
using Headless.Settings.Resources;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Helpers;

/// <summary>Default implementation of <see cref="ISettingEncryptionService"/> backed by <see cref="IStringEncryptionService"/>.</summary>
public sealed class SettingEncryptionService(
    IStringEncryptionService stringEncryptionService,
    ISettingErrorsDescriptor errorsDescriptor,
    ILogger<SettingEncryptionService> logger
) : ISettingEncryptionService
{
    /// <inheritdoc/>
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

    /// <inheritdoc/>
    /// <exception cref="Headless.Exceptions.ConflictException">The underlying decryption fails.</exception>
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

            throw new ConflictException(errorsDescriptor.DecryptionFailed(settingDefinition.Name));
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
