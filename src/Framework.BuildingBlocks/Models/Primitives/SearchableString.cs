// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Framework.Domains;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
[ComplexType]
[DebuggerDisplay("{" + nameof(_value) + "}")]
public sealed class SearchableString : ValueObject
{
    private string? _value;

    public SearchableString(string value)
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

    public SearchableString ToSearchableString() => new(Value);

    public static SearchableString FromString(string value) => value;

    [return: NotNullIfNotNull(nameof(value))]
    public static string? Normalize(string? value) => value?.Trim().SearchString();

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator string?(SearchableString? value) => value?.Value;

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator SearchableString?(string? value) => value is null ? null : new(value);
}
