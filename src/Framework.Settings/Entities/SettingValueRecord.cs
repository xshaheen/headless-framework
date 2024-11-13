// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Settings.Entities;

public sealed class SettingValueRecord : Entity<Guid>, IAggregateRoot<Guid>
{
    private SettingValueRecord()
    {
        Name = default!;
        Value = default!;
        ProviderName = default!;
    }

    public SettingValueRecord(Guid id, string name, string value, string providerName, string? providerKey = null)
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(value);
        Argument.IsNotNull(providerName);

        Id = id;
        Name = name;
        Value = value;
        ProviderName = providerName;
        ProviderKey = providerKey;
    }

    public string Name { get; private set; }

    public string Value { get; internal set; }

    public string ProviderName { get; private set; }

    public string? ProviderKey { get; private set; }

    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
