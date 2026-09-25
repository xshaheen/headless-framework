// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Headless.Checks;

namespace Headless.Security;

/// <summary>A named parameter of a <see cref="PhcString" />, such as <c>m=19456</c>.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Value">The parameter value, as written.</param>
[PublicAPI]
public readonly record struct PhcParameter(string Name, string Value);

/// <summary>
/// A parsed <see href="https://github.com/C2SP/C2SP/blob/main/phc-strings.md">PHC string</see> carrying a salt and a
/// hash: <c>$&lt;id&gt;[$v=&lt;version&gt;][$&lt;name&gt;=&lt;value&gt;(,&lt;name&gt;=&lt;value&gt;)*]$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Salt and hash use standard base64 without padding. Parsing accepts only the canonical form — no padding, no
/// non-zero unused bits, no empty segments, no duplicate parameter names — so every accepted value has exactly one
/// encoding and round-trips byte for byte.
/// </para>
/// <para>
/// Parsing never throws on malformed input, which makes it safe to run on values read from storage. It validates the
/// shape only: the algorithm that owns <see cref="Id" /> decides which parameters and ranges it accepts.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class PhcString
{
    /// <summary>The longest string <see cref="TryParse" /> accepts, in characters.</summary>
    public const int MaxLength = 512;

    private const int _MaxNameLength = 32;

    private readonly byte[] _salt;
    private readonly byte[] _hash;

    // The instance is immutable, and the constructor must format it anyway to enforce MaxLength.
    private readonly string _encoded;

    /// <summary>Creates a PHC string from its parts.</summary>
    /// <param name="id">The algorithm identifier: 1 to 32 characters from <c>[a-z0-9-]</c>.</param>
    /// <param name="version">The optional algorithm version, emitted as <c>$v=&lt;version&gt;</c>.</param>
    /// <param name="parameters">The parameters, emitted in the given order.</param>
    /// <param name="salt">The salt. Must not be empty.</param>
    /// <param name="hash">The hash. Must not be empty.</param>
    /// <exception cref="ArgumentException">A part violates the PHC grammar or the result exceeds <see cref="MaxLength" />.</exception>
    public PhcString(
        string id,
        int? version,
        IReadOnlyList<PhcParameter> parameters,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> hash
    )
    {
        Argument.IsNotNull(id);
        Argument.IsNotNull(parameters);

        if (!_IsName(id))
        {
            throw new ArgumentException("The PHC id must be 1 to 32 characters from [a-z0-9-].", nameof(id));
        }

        if (version < 0)
        {
            throw new ArgumentException("The PHC version must not be negative.", nameof(version));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (!_IsName(parameter.Name) || !_IsValue(parameter.Value) || !seen.Add(parameter.Name))
            {
                throw new ArgumentException(
                    "Each PHC parameter needs a unique [a-z0-9-] name and a non-empty [a-zA-Z0-9/+.-] value.",
                    nameof(parameters)
                );
            }
        }

        Argument.IsNotEmpty(salt, "The salt must not be empty.");
        Argument.IsNotEmpty(hash, "The hash must not be empty.");

        Id = id;
        Version = version;
        Parameters = [.. parameters];
        _salt = salt.ToArray();
        _hash = hash.ToArray();
        _encoded = _Format();

        if (_encoded.Length > MaxLength)
        {
            throw new ArgumentException($"The encoded PHC string exceeds {MaxLength} characters.", nameof(hash));
        }
    }

    /// <summary>Gets the algorithm identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the algorithm version, or <see langword="null" /> when the string has no version segment.</summary>
    public int? Version { get; }

    /// <summary>Gets the parameters in encoded order.</summary>
    public IReadOnlyList<PhcParameter> Parameters { get; }

    /// <summary>Gets the decoded salt.</summary>
    public ReadOnlySpan<byte> Salt => _salt;

    /// <summary>Gets the decoded hash.</summary>
    public ReadOnlySpan<byte> Hash => _hash;

    /// <summary>Reads a parameter as a canonical non-negative decimal integer.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns>
    /// <see langword="true" /> when the parameter exists and is a decimal without leading zeros that fits an
    /// <see cref="int" />.
    /// </returns>
    public bool TryGetInt32(string name, out int value)
    {
        foreach (var parameter in Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return _TryParseDecimal(parameter.Value, out value);
            }
        }

        value = 0;

        return false;
    }

    /// <summary>Parses a PHC string that carries a salt and a hash.</summary>
    /// <param name="value">The encoded value. May be <see langword="null" />.</param>
    /// <param name="result">The parsed value, when parsing succeeds.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is a canonical PHC string.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out PhcString? result)
    {
        result = null;

        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value[0] != '$')
        {
            return false;
        }

        // "$id$v=19$m=1,t=2$salt$hash" splits into ["", id, v=19, params, salt, hash]; the leading empty entry is the
        // required '$' prefix. Everything between the id and the salt is optional: a version, a parameter list, or both.
        var segments = value.Split('$');

        if (segments.Length is < 4 or > 6)
        {
            return false;
        }

        var id = segments[1];

        if (!_IsName(id))
        {
            return false;
        }

        int? version = null;
        var parameters = new List<PhcParameter>();
        var middle = segments.AsSpan(2, segments.Length - 4);

        if (!middle.IsEmpty && middle[0].StartsWith("v=", StringComparison.Ordinal))
        {
            if (!_TryParseDecimal(middle[0].AsSpan(2), out var parsedVersion))
            {
                return false;
            }

            version = parsedVersion;
            middle = middle[1..];
        }

        if (middle.Length > 1 || (middle.Length == 1 && !_TryParseParameters(middle[0], parameters)))
        {
            return false;
        }

        if (!_TryDecodeBase64(segments[^2], out var salt) || !_TryDecodeBase64(segments[^1], out var hash))
        {
            return false;
        }

        result = new PhcString(id, version, parameters, salt, hash);

        return true;
    }

    /// <summary>Formats the PHC string in canonical form.</summary>
    /// <returns>The encoded value.</returns>
    public override string ToString()
    {
        return _encoded;
    }

    private string _Format()
    {
        var builder = new StringBuilder();
        builder.Append('$').Append(Id);

        if (Version is { } version)
        {
            builder.Append("$v=").Append(version.ToString(CultureInfo.InvariantCulture));
        }

        if (Parameters.Count > 0)
        {
            builder.Append('$');

            for (var i = 0; i < Parameters.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Parameters[i].Name).Append('=').Append(Parameters[i].Value);
            }
        }

        builder.Append('$').Append(_EncodeBase64(_salt));
        builder.Append('$').Append(_EncodeBase64(_hash));

        return builder.ToString();
    }

    private static bool _TryParseParameters(string segment, List<PhcParameter> parameters)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in segment.Split(','))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                return false;
            }

            var name = pair[..separator];
            var parameterValue = pair[(separator + 1)..];

            if (!_IsName(name) || !_IsValue(parameterValue) || !seen.Add(name))
            {
                return false;
            }

            parameters.Add(new PhcParameter(name, parameterValue));
        }

        return true;
    }

    private static bool _TryParseDecimal(ReadOnlySpan<char> text, out int value)
    {
        value = 0;

        // PHC decimals have no sign and no leading zeros, so "0" is the only value that may start with '0'.
        if (text.IsEmpty || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool _TryDecodeBase64(string text, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;

        // Unpadded base64 never leaves a single trailing character; anything else outside the standard alphabet
        // (padding included) is rejected up front so Convert only ever sees canonical-alphabet input.
        if (text.Length == 0 || text.Length % 4 == 1)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '/')
            {
                return false;
            }
        }

        var padded = (text.Length % 4) switch
        {
            2 => text + "==",
            3 => text + "=",
            _ => text,
        };

        var buffer = new byte[padded.Length / 4 * 3];

        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return false;
        }

        bytes = buffer.AsSpan(0, written).ToArray();

        // The BCL decoder ignores non-zero unused bits in the last character, which would give one byte sequence
        // several spellings. Re-encoding and comparing keeps exactly one accepted spelling per value.
        return string.Equals(_EncodeBase64(bytes), text, StringComparison.Ordinal);
    }

    private static string _EncodeBase64(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=');
    }

    private static bool _IsName(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > _MaxNameLength)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool _IsValue(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '/' && c != '+' && c != '.' && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}
