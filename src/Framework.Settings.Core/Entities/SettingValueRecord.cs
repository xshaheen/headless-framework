// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Domain;

namespace Framework.Settings.Entities;

public sealed class SettingValueRecord : Entity<Guid>, IAggregateRoot<Guid>
{
    [UsedImplicitly]
    private SettingValueRecord()
    {
        Name = null!;
        Value = null!;
        ProviderName = null!;
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

    public string Name { get; private init; }

    public string Value { get; internal set; }

    public string ProviderName { get; private init; }

    public string? ProviderKey { get; private init; }

    // TODO: Add DateCreated, DateUpdated, CreatedBy, UpdatedBy, ...

    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
