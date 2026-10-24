// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
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

    private readonly FrozenDictionary<string, (ulong Bits, string Name)> _names;
    private readonly FrozenDictionary<string, (ulong Bits, string Name)> _aliases;
    private readonly bool _isFlags;

    private RequestEnumValue(
        Type enumType,
        FrozenDictionary<string, (ulong Bits, string Name)> names,
        FrozenDictionary<string, (ulong Bits, string Name)> aliases
    )
    {
        EnumType = enumType;
        _names = names;
        _aliases = aliases;
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
        if (_TryGetBits(value, out var bits, out _))
        {
            result = Enum.ToObject(EnumType, bits);

            return true;
        }

        result = null;

        return false;
    }

    /// <summary>
    /// Parses <paramref name="value"/> and returns the text <c>Enum.TryParse</c> accepts for the same member: the member
    /// name, or the comma-separated member names of a <c>[Flags]</c> combination.
    /// </summary>
    /// <returns><see langword="true"/> when the body would accept <paramref name="value"/>.</returns>
    public bool TryGetCanonical(string value, out string canonical)
    {
        if (!_TryGetBits(value, out var bits, out var singleName))
        {
            canonical = string.Empty;

            return false;
        }

        canonical = singleName ?? Enum.ToObject(EnumType, bits).ToString()!;

        return true;
    }

    private bool _TryGetBits(string value, out ulong bits, out string? singleName)
    {
        var trimmed = value.AsSpan().Trim();

        if (_TryFind(trimmed, out var member))
        {
            bits = member.Bits;
            singleName = member.Name;

            return true;
        }

        bits = 0;
        singleName = null;

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

    private bool _TryFind(ReadOnlySpan<char> name, out (ulong Bits, string Name) member) =>
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
        var names = new Dictionary<string, (ulong, string)>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, (ulong, string)>(StringComparer.Ordinal);

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
            (isAlias ? aliases : names).TryAdd(wireName, (_ToBits(member), memberName));
        }

        return new RequestEnumValue(
            enumType,
            names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            aliases.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }

    // Two's-complement bits, so a negative member of a signed enum round-trips through Enum.ToObject.
    private static ulong _ToBits(object member) =>
        Type.GetTypeCode(Enum.GetUnderlyingType(member.GetType())) is TypeCode.UInt64
            ? Convert.ToUInt64(member, CultureInfo.InvariantCulture)
            : unchecked((ulong)Convert.ToInt64(member, CultureInfo.InvariantCulture));
}
