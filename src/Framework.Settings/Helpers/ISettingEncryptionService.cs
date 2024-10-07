// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Definitions;
using Framework.Settings.Models;
using Microsoft.Extensions.Logging;

namespace Framework.Settings.Helpers;

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

        return stringEncryptionService.Encrypt(plainValue);
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
            logger.LogWarning(e, "Failed to decrypt setting value: {SettingDefinition}", settingDefinition.Name);

            return null;
        }
    }
}
