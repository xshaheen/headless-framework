// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Headless.Api;

/// <summary>
/// Reads an enum from a route, query, header, or form value by the names the JSON body accepts for it, so every request
/// source agrees with the body.
/// </summary>
/// <remarks>
/// <para>
/// The names come from the application's serializer options: each member is written once through the configured enum
/// converter, so the configured naming policy and a <c>[JsonStringEnumMemberName]</c> alias decide its name, exactly as
/// they do for the body. A name the policy derives matches in any casing, while an alias matches only as written, because
/// that is how the body reads them; a <c>[Flags]</c> value may list names separated by commas, and a number matches
/// nothing, as the Headless converter disallows integer values.
/// </para>
/// <para>
/// The table is built on first use per enum type and options instance, so a request costs one dictionary lookup per
/// name and allocates nothing for a value that is already canonical.
/// </para>
/// </remarks>
internal sealed class RequestEnumValue
{
    private static readonly ConcurrentDictionary<(Type, JsonSerializerOptions), RequestEnumValue?> _Cache = new();

    private readonly FrozenDictionary<string, Member> _names;
    private readonly FrozenDictionary<string, Member> _aliases;
    private readonly FrozenDictionary<string, ExactName> _exact;
    private readonly bool _isFlags;

    private RequestEnumValue(
        Type enumType,
        FrozenDictionary<string, Member> names,
        FrozenDictionary<string, Member> aliases,
        FrozenDictionary<string, ExactName> exact
    )
    {
        EnumType = enumType;
        _names = names;
        _aliases = aliases;
        _exact = exact;
        _isFlags = enumType.IsDefined(typeof(FlagsAttribute), inherit: false);
    }

    /// <summary>The enum type, not its nullable wrapper.</summary>
    public Type EnumType { get; }

    /// <summary>
    /// Gets the names for <paramref name="enumType"/>, or <see langword="null"/> when the options write it as a number,
    /// in which case the body accepts no names and the request sources keep the host's own parsing.
    /// </summary>
    public static RequestEnumValue? For(Type enumType, JsonSerializerOptions options) =>
        _Cache.GetOrAdd((enumType, options), static key => _Build(key.Item1, key.Item2));

    /// <summary>Parses <paramref name="value"/> to the member the body would read from the same JSON string.</summary>
    public bool TryParse(string value, out object? result)
    {
        if (_TryGetBits(value, out var bits, out var single))
        {
            // A single member returns the value boxed when the table was built, so binding it allocates nothing.
            result = single?.Boxed ?? Enum.ToObject(EnumType, bits);

            return true;
        }

        result = null;

        return false;
    }

    /// <summary>
    /// Checks <paramref name="value"/> for a binder that hands the text on to <c>Enum.TryParse</c>, which is
    /// case-sensitive: the value must be spelled exactly as the body writes it or as the member is declared.
    /// </summary>
    /// <param name="value">The raw request value, not empty.</param>
    /// <param name="rewrite">
    /// <see langword="null"/> when <c>Enum.TryParse</c> reads <paramref name="value"/> as it is; otherwise the member
    /// name to put in its place, for an alias or a name the naming policy derived.
    /// </param>
    /// <returns>
    /// <see langword="false"/> for a number, an unknown name, a name in other casing, or a member name an alias replaces.
    /// </returns>
    /// <remarks>
    /// When the wire names are the member names, which the Headless web preset writes, every accepted single value
    /// passes through as it is: one lookup, nothing allocated. Only a <c>[Flags]</c> list with a part to rewrite builds a
    /// new string.
    /// </remarks>
    public bool TryGetExact(string value, out string? rewrite)
    {
        var lookup = _exact.GetAlternateLookup<ReadOnlySpan<char>>();
        var trimmed = value.AsSpan().Trim();

        if (lookup.TryGetValue(trimmed, out var exact))
        {
            rewrite = exact.IsMemberName ? null : exact.Member.Name;

            return true;
        }

        rewrite = null;

        if (!_isFlags || trimmed.IndexOf(',') < 0)
        {
            return false;
        }

        ulong bits = 0;
        var rewritten = false;

        foreach (var range in trimmed.Split(','))
        {
            if (!lookup.TryGetValue(trimmed[range].Trim(), out var part))
            {
                return false;
            }

            bits |= part.Member.Bits;
            rewritten |= !part.IsMemberName;
        }

        if (rewritten)
        {
            rewrite = Enum.ToObject(EnumType, bits).ToString();
        }

        return true;
    }

    private bool _TryGetBits(string value, out ulong bits, out Member? single)
    {
        var trimmed = value.AsSpan().Trim();

        if (_TryFind(trimmed, out var member))
        {
            bits = member.Bits;
            single = member;

            return true;
        }

        bits = 0;
        single = null;

        if (!_isFlags || trimmed.IndexOf(',') < 0)
        {
            return false;
        }

        foreach (var range in trimmed.Split(','))
        {
            if (!_TryFind(trimmed[range].Trim(), out var part))
            {
                bits = 0;

                return false;
            }

            bits |= part.Bits;
        }

        return true;
    }

    private bool _TryFind(ReadOnlySpan<char> name, [MaybeNullWhen(false)] out Member member) =>
        _aliases.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out member)
        || _names.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out member);

    private static RequestEnumValue? _Build(Type enumType, JsonSerializerOptions options)
    {
        // Options built without a resolver get the default one when the body is first read; a copy gets it here, so
        // the application's own instance is left as it is.
        if (options.TypeInfoResolver is null)
        {
            options = new JsonSerializerOptions(options);
            options.MakeReadOnly(populateMissingResolver: true);
        }

        var typeInfo = options.GetTypeInfo(enumType);
        var names = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, Member>(StringComparer.Ordinal);
        var exact = new Dictionary<string, ExactName>(StringComparer.Ordinal);

        foreach (var member in Enum.GetValues(enumType))
        {
            var json = JsonSerializer.Serialize(member, typeInfo);

            // A converter that writes the member as a number gives the body no names to accept.
            if (json.Length < 2 || json[0] != '"')
            {
                return null;
            }

            var wireName = JsonSerializer.Deserialize<string>(json)!;
            var memberName = member.ToString()!;
            var isAlias = enumType.GetField(memberName)?.IsDefined(typeof(JsonStringEnumMemberNameAttribute)) == true;
            var entry = new Member(_ToBits(member), memberName, member);
            (isAlias ? aliases : names).TryAdd(wireName, entry);
            exact.TryAdd(
                wireName,
                new ExactName(entry, IsMemberName: string.Equals(wireName, memberName, StringComparison.Ordinal))
            );

            // The declared name is what Enum.TryParse reads, so it passes as it is, unless an alias replaced it: the
            // body rejects a replaced name, and so does every other source.
            if (!isAlias)
            {
                exact.TryAdd(memberName, new ExactName(entry, IsMemberName: true));
            }
        }

        return new RequestEnumValue(
            enumType,
            names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            aliases.ToFrozenDictionary(StringComparer.Ordinal),
            exact.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }

    // Two's-complement bits, so a negative member of a signed enum round-trips through Enum.ToObject.
    private static ulong _ToBits(object member) =>
        Type.GetTypeCode(Enum.GetUnderlyingType(member.GetType())) is TypeCode.UInt64
            ? Convert.ToUInt64(member, CultureInfo.InvariantCulture)
            : unchecked((ulong)Convert.ToInt64(member, CultureInfo.InvariantCulture));

    /// <summary>One accepted name: the member's bits, its name as <c>Enum.TryParse</c> reads it, and its boxed value.</summary>
    private sealed record Member(ulong Bits, string Name, object Boxed);

    /// <summary>A spelling accepted as written, and whether it is already the name <c>Enum.TryParse</c> reads.</summary>
    private sealed record ExactName(Member Member, bool IsMemberName);
}
