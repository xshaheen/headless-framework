// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Settings.Entities;

public sealed class SettingValueRecord : Entity<Guid>, IAggregateRoot<Guid>, ICreateAudit, IUpdateAudit
{
    [SetsRequiredMembers]
    [UsedImplicitly]
    private SettingValueRecord()
    {
        Name = null!;
        Value = null!;
        ProviderName = null!;
    }

    [SetsRequiredMembers]
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

    public static SettingValueRecord FromStorage(
        Guid id,
        string name,
        string value,
        string providerName,
        string? providerKey,
        DateTimeOffset dateCreated,
        DateTimeOffset? dateUpdated
    )
    {
        return new SettingValueRecord(id, name, value, providerName, providerKey)
        {
            DateCreated = dateCreated,
            DateUpdated = dateUpdated,
        };
    }

    public string Name { get; private init; }

    public string Value { get; internal set; }

    public string ProviderName { get; private init; }

    public string? ProviderKey { get; private init; }

    public DateTimeOffset DateCreated { get; private set; }

    public DateTimeOffset? DateUpdated { get; private set; }

    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
