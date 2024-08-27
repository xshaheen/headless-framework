namespace Framework.Settings.ValueStores;

public sealed record SettingValue(string Name)
{
    public SettingValue(string name, string? value)
        : this(name) => Value = value;

    public string? Value { get; set; }
}
