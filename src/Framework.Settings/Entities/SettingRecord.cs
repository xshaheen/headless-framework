using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Settings.Entities;

public sealed class SettingRecord : Entity<Guid>, IAggregateRoot<Guid>
{
    private SettingRecord()
    {
        Name = default!;
        Value = default!;
    }

    public SettingRecord(Guid id, string name, string value, string? providerName = null, string? providerKey = null)
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(value);

        Id = id;
        Name = name;
        Value = value;
        ProviderName = providerName;
        ProviderKey = providerKey;
    }

    public string Name { get; private set; }

    public string Value { get; internal set; }

    public string? ProviderName { get; private set; }

    public string? ProviderKey { get; private set; }

    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
