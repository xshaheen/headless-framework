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
            logger.LogWarning(e, "Failed to encrypt setting value: {SettingDefinition}", settingDefinition.Name);

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
            logger.LogError(e, "Failed to decrypt setting value: {SettingDefinition}", settingDefinition.Name);

            throw new ConflictException($@"Failed to decrypt setting '{settingDefinition.Name}'.", e);
        }
    }
}
