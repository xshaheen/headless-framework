using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Framework.BuildingBlocks.Domains;

namespace Framework.BuildingBlocks.Primitives;

[PublicAPI]
[DebuggerDisplay("{" + nameof(_value) + "}")]
public sealed class NormalizedString : ValueObject
{
    private string? _value;

    public NormalizedString(string value)
    {
        Value = value.Trim();
    }

    public string Value
    {
        get => _value!;
        set
        {
            _value = value.Trim();
            Normalized = Normalize(_value);
        }
    }

    public string Normalized { get; private set; } = default!;

    protected override IEnumerable<object> EqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public NormalizedString ToNormalizedString() => new(Value);

    public static NormalizedString FromString(string value) => value;

    [return: NotNullIfNotNull(nameof(value))]
    public static string? Normalize(string? value) => value?.Trim().SearchString();

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(NormalizedString? value) => value?.Value;

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator NormalizedString?(string? value) => value is null ? null : new(value);
}
