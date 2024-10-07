// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Settings.Values;

/// <summary>Represents a store for setting values.</summary>
public interface ISettingValueStore
{
    Task<string?> GetOrDefaultAsync(string name, string? providerName, string? providerKey);

    Task<List<SettingValue>> GetAllAsync(string[] names, string? providerName, string? providerKey);
}
