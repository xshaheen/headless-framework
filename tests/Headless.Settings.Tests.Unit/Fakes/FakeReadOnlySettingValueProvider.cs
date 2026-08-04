// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Headless.Settings.ValueProviders;

namespace Tests.Fakes;

/// <summary>
/// Read-only provider fake modelling plaintext sources such as the definition-default and configuration
/// providers: it never receives manager-encrypted writes, so <c>StoresEncryptedValues</c> stays
/// <see langword="false"/> and encrypted definitions resolved from it must not be decrypted.
/// </summary>
public sealed class FakeReadOnlySettingValueProvider : ISettingValueReadProvider
{
    private readonly Dictionary<(string Name, string? Key), string?> _values = [];

    public string Name { get; init; } = "FakeReadOnly";

    public Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(_values.GetValueOrDefault((setting.Name, providerKey)));
    }

    public Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            settings.Select(d => new SettingValue(d.Name, _values.GetValueOrDefault((d.Name, providerKey)))).ToList()
        );
    }

    public void SetValue(string settingName, string? value, string? providerKey = null)
    {
        _values[(settingName, providerKey)] = value;
    }
}
