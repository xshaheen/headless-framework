// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Settings.Models;

public sealed record SettingValue(string Name)
{
    public SettingValue(string name, string? value)
        : this(name) => Value = value;

    public string? Value { get; set; }
}
