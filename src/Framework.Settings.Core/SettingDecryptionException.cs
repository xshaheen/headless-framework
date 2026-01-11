// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Settings;

/// <summary>
/// Thrown when decryption of an encrypted setting value fails.
/// </summary>
public sealed class SettingDecryptionException : Exception
{
    public string SettingName { get; }

    public SettingDecryptionException(string settingName, Exception innerException)
        : base($"Failed to decrypt setting '{settingName}'.", innerException)
    {
        SettingName = settingName;
    }
}
