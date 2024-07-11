using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using Framework.BuildingBlocks.Domains;

namespace Framework.BuildingBlocks.Primitives;

[ComplexType]
[DebuggerDisplay("{" + nameof(Language) + "}-{" + nameof(Country) + "}")]
public sealed class PreferredLocale : ValueObject
{
    public static readonly PreferredLocale ArEg = new("EG", "ar");
    public static readonly PreferredLocale EnUs = new("US", "en");

    private PreferredLocale()
    {
        Country = default!;
        Language = default!;
    }

    public PreferredLocale(string country, string language)
    {
        Country = country;
        Language = language;
    }

    /// <summary>Three-letter ISO country code in uppercase.</summary>
    public string Country { get; private init; }

    /// <summary>Two-letter ISO language code in lowercase.</summary>
    public string Language { get; private init; }

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Country;
        yield return Language;
    }

    public override string ToString() => $"{Language}-{Country}";
}
