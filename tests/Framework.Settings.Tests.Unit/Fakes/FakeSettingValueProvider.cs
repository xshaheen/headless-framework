// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;
using Framework.Settings.ValueProviders;

namespace Tests.Fakes;

public sealed class FakeSettingValueProvider : ISettingValueProvider
{
    private readonly Dictionary<(string Name, string? Key), string?> _values = [];

    public string Name { get; init; } = "Fake";

    public Task<string?> GetOrDefaultAsync(
        SettingDefinition setting,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_values.GetValueOrDefault((setting.Name, providerKey)));

    public Task SetAsync(
        SettingDefinition setting,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _values[(setting.Name, providerKey)] = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync(
        SettingDefinition setting,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        _values.Remove((setting.Name, providerKey));
        return Task.CompletedTask;
    }

    public Task<List<SettingValue>> GetAllAsync(
        SettingDefinition[] settings,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            settings.Select(d => new SettingValue(d.Name, _values.GetValueOrDefault((d.Name, providerKey)))).ToList()
        );

    public void SetValue(string settingName, string? value, string? providerKey = null) =>
        _values[(settingName, providerKey)] = value;

    public void Clear() => _values.Clear();
}
