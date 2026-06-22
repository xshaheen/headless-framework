// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Exceptions;
using Headless.Settings.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Settings.Helpers;

/// <summary>Encrypts and decrypts setting values for settings that have <c>IsEncrypted</c> set to <see langword="true"/>.</summary>
public interface ISettingEncryptionService
{
    /// <summary>Encrypts <paramref name="plainValue"/> for the given <paramref name="settingDefinition"/>.</summary>
    /// <param name="settingDefinition">The setting whose value is being encrypted.</param>
    /// <param name="plainValue">The plain-text value to encrypt, or <see langword="null"/> / empty to pass through unchanged.</param>
    /// <returns>The encrypted value, or the original value when it is <see langword="null"/> or empty.</returns>
    string? Encrypt(SettingDefinition settingDefinition, string? plainValue);

    /// <summary>Decrypts <paramref name="encryptedValue"/> for the given <paramref name="settingDefinition"/>.</summary>
    /// <param name="settingDefinition">The setting whose value is being decrypted.</param>
    /// <param name="encryptedValue">The encrypted value to decrypt, or <see langword="null"/> / empty to pass through unchanged.</param>
    /// <returns>The decrypted plain-text value, or the original value when it is <see langword="null"/> or empty.</returns>
    /// <exception cref="Headless.Exceptions.ConflictException">Decryption of the setting value fails.</exception>
    string? Decrypt(SettingDefinition settingDefinition, string? encryptedValue);
}

/// <summary>Default implementation of <see cref="ISettingEncryptionService"/> backed by <see cref="IStringEncryptionService"/>.</summary>
public sealed class SettingEncryptionService(
    IStringEncryptionService stringEncryptionService,
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

            throw new ConflictException($"Failed to decrypt setting '{settingDefinition.Name}'.", e);
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
