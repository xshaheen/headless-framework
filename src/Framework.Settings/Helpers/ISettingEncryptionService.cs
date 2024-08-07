using Framework.Settings.DefinitionProviders;

namespace Framework.Settings.Helpers;

public interface ISettingEncryptionService
{
    string? Encrypt(SettingDefinition settingDefinition, string? plainValue);

    string? Decrypt(SettingDefinition settingDefinition, string? encryptedValue);
}
