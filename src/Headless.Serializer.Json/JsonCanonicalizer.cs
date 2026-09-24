// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.Serializer;

/// <summary>
/// Canonicalizes JSON per RFC 8785 (JSON Canonicalization Scheme) and hashes the canonical form with SHA-256,
/// producing fingerprints and idempotency keys that any RFC 8785 implementation reproduces byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The canonical form has no insignificant whitespace, object members sorted by the UTF-16 code units of their
/// names, numbers in the ECMAScript <c>Number.prototype.toString</c> form of their IEEE-754 double value, and strings
/// that escape only <c>"</c>, <c>\</c>, and U+0000–U+001F (as <c>\b \t \n \f \r</c> where one exists, otherwise
/// <c>\u00xx</c> in lowercase hex). Every other character, including non-ASCII text, is written as literal UTF-8.
/// </para>
/// <para>
/// Numbers are IEEE-754 doubles, as RFC 8785 specifies: a fraction or exponent literal rounds to the nearest double,
/// so <c>0.10000000000000001</c> and <c>0.1</c> canonicalize alike. An integer literal outside ±(2^53 − 1) is
/// rejected with a <see cref="JsonException"/> instead, because rounding would give distinct identifiers one hash;
/// send such values, and exact decimals, as JSON strings. This matches the Python <c>rfc8785</c> package. Duplicate
/// member names, numbers outside the double range, and lone UTF-16 surrogates are rejected as well.
/// </para>
/// <para>
/// Hash the JSON as sent or stored. Serializing an object first makes the hash depend on the serializer's naming
/// policy and converters, which is how independently written copies of a hash drift apart.
/// </para>
/// </remarks>
[PublicAPI]
public static class JsonCanonicalizer
{
    /// <summary>The size in bytes of the SHA-256 hash that <c>Hash</c> produces.</summary>
    public const int HashSizeInBytes = SHA256.HashSizeInBytes;

    // Lone surrogates must fail rather than become U+FFFD, or two different documents would canonicalize alike.
    private static readonly UTF8Encoding _StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    // Number.MAX_SAFE_INTEGER: every integer up to it has its own double; above it, neighbors collapse together.
    private const double _MaxSafeInteger = 9007199254740991;

    // JsonDocument defaults to 64 levels, which rejects documents other languages' parsers accept.
    private const int _MaxDepth = 1000;

    // Error messages echo input, which has no size limit, so they carry only its start.
    private const int _MaxEchoedLength = 64;

    private static readonly JsonDocumentOptions _ParseOptions = new()
    {
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = _MaxDepth,
    };

    /// <summary>Writes the RFC 8785 canonical form of <paramref name="element"/> to <paramref name="output"/>.</summary>
    /// <param name="element">The JSON value to canonicalize.</param>
    /// <param name="output">The UTF-8 destination.</param>
    /// <exception cref="ArgumentException"><paramref name="element"/> is <see langword="default"/>.</exception>
    /// <exception cref="JsonException">The value cannot be canonicalized without loss.</exception>
    public static void Canonicalize(JsonElement element, IBufferWriter<byte> output)
    {
        Argument.IsNotNull(output);

        if (element.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException("The JSON element is undefined (default).", nameof(element));
        }

        _WriteValue(element, output, []);
    }

    /// <summary>Parses <paramref name="utf8Json"/> and writes its RFC 8785 canonical form to <paramref name="output"/>.</summary>
    /// <param name="utf8Json">
    /// One UTF-8 JSON value, nested at most 1,000 levels deep. Comments, trailing commas, and trailing content are rejected.
    /// </param>
    /// <param name="output">The UTF-8 destination.</param>
    /// <exception cref="JsonException">The input is not valid JSON or cannot be canonicalized without loss.</exception>
    public static void Canonicalize(ReadOnlySpan<byte> utf8Json, IBufferWriter<byte> output)
    {
        Argument.IsNotNull(output);

        JsonElement element;

        try
        {
            element = JsonElement.Parse(utf8Json, _ParseOptions);
        }
        catch (InvalidOperationException e)
        {
            // The duplicate-member check decodes names, so a lone surrogate in a name surfaces here, not as JsonException.
            throw new JsonException("Cannot canonicalize JSON: a member name is not valid UTF-16.", e);
        }

        _WriteValue(element, output, []);
    }

    /// <summary>Returns the RFC 8785 canonical form of <paramref name="element"/> as UTF-8 bytes.</summary>
    /// <inheritdoc cref="Canonicalize(JsonElement, IBufferWriter{byte})"/>
    public static byte[] Canonicalize(JsonElement element)
    {
        using var buffer = new PooledByteBufferWriter();
        Canonicalize(element, buffer);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Returns the RFC 8785 canonical form of <paramref name="utf8Json"/> as UTF-8 bytes.</summary>
    /// <inheritdoc cref="Canonicalize(ReadOnlySpan{byte}, IBufferWriter{byte})"/>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        using var buffer = new PooledByteBufferWriter();
        Canonicalize(utf8Json, buffer);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Returns the SHA-256 hash of the RFC 8785 canonical form of <paramref name="element"/>.</summary>
    /// <param name="element">The JSON value to hash.</param>
    /// <returns>A new <see cref="HashSizeInBytes"/>-byte array.</returns>
    /// <exception cref="ArgumentException"><paramref name="element"/> is <see langword="default"/>.</exception>
    /// <exception cref="JsonException">The value cannot be canonicalized without loss.</exception>
    public static byte[] Hash(JsonElement element)
    {
        var hash = new byte[HashSizeInBytes];
        Hash(element, hash);

        return hash;
    }

    /// <summary>Returns the SHA-256 hash of the RFC 8785 canonical form of <paramref name="utf8Json"/>.</summary>
    /// <param name="utf8Json">One UTF-8 JSON value. Comments, trailing commas, and trailing content are rejected.</param>
    /// <returns>A new <see cref="HashSizeInBytes"/>-byte array.</returns>
    /// <exception cref="JsonException">The input is not valid JSON or cannot be canonicalized without loss.</exception>
    public static byte[] Hash(ReadOnlySpan<byte> utf8Json)
    {
        var hash = new byte[HashSizeInBytes];
        Hash(utf8Json, hash);

        return hash;
    }

    /// <summary>Writes the SHA-256 hash of the RFC 8785 canonical form of <paramref name="element"/> to <paramref name="destination"/>.</summary>
    /// <param name="element">The JSON value to hash.</param>
    /// <param name="destination">Receives the hash in its first <see cref="HashSizeInBytes"/> bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="element"/> is <see langword="default"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than <see cref="HashSizeInBytes"/>.</exception>
    /// <exception cref="JsonException">The value cannot be canonicalized without loss.</exception>
    public static void Hash(JsonElement element, Span<byte> destination)
    {
        Argument.IsGreaterThanOrEqualTo(destination.Length, HashSizeInBytes, paramName: nameof(destination));

        using var buffer = new PooledByteBufferWriter();
        Canonicalize(element, buffer);
        SHA256.HashData(buffer.WrittenSpan, destination);
    }

    /// <summary>Writes the SHA-256 hash of the RFC 8785 canonical form of <paramref name="utf8Json"/> to <paramref name="destination"/>.</summary>
    /// <param name="utf8Json">One UTF-8 JSON value. Comments, trailing commas, and trailing content are rejected.</param>
    /// <param name="destination">Receives the hash in its first <see cref="HashSizeInBytes"/> bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than <see cref="HashSizeInBytes"/>.</exception>
    /// <exception cref="JsonException">The input is not valid JSON or cannot be canonicalized without loss.</exception>
    public static void Hash(ReadOnlySpan<byte> utf8Json, Span<byte> destination)
    {
        Argument.IsGreaterThanOrEqualTo(destination.Length, HashSizeInBytes, paramName: nameof(destination));

        using var buffer = new PooledByteBufferWriter();
        Canonicalize(utf8Json, buffer);
        SHA256.HashData(buffer.WrittenSpan, destination);
    }

    private static void _WriteValue(
        JsonElement element,
        IBufferWriter<byte> output,
        List<(string? Name, int Index)> path
    )
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                _WriteObject(element, output, path);
                break;
            case JsonValueKind.Array:
                _WriteArray(element, output, path);
                break;
            case JsonValueKind.String:
                _WriteString(_GetString(element, path), output, path);
                break;
            case JsonValueKind.Number:
                _WriteNumber(JsonMarshal.GetRawUtf8Value(element), output, path);
                break;
            case JsonValueKind.True:
                _WriteRaw("true"u8, output);
                break;
            case JsonValueKind.False:
                _WriteRaw("false"u8, output);
                break;
            case JsonValueKind.Null:
                _WriteRaw("null"u8, output);
                break;
            default:
                throw _Error(path, $"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static void _WriteObject(
        JsonElement element,
        IBufferWriter<byte> output,
        List<(string? Name, int Index)> path
    )
    {
        var members = new List<KeyValuePair<string, JsonElement>>();

        foreach (var property in element.EnumerateObject())
        {
            string name;

            try
            {
                name = property.Name;
            }
            catch (InvalidOperationException e)
            {
                throw _Error(path, "A member name is not valid UTF-16.", e);
            }

            members.Add(new(name, property.Value));
        }

        // RFC 8785 orders members by UTF-16 code units, which is exactly what an ordinal string comparison does;
        // UTF-8 byte order differs for U+E000-U+FFFF against supplementary-plane characters.
        members.Sort(static (x, y) => string.CompareOrdinal(x.Key, y.Key));

        _WriteByte((byte)'{', output);

        for (var i = 0; i < members.Count; i++)
        {
            var (name, value) = members[i];

            if (i > 0)
            {
                if (string.Equals(members[i - 1].Key, name, StringComparison.Ordinal))
                {
                    throw _Error(path, $"Duplicate member name '{_Echo(name)}'.");
                }

                _WriteByte((byte)',', output);
            }

            path.Add((name, 0));
            _WriteString(name, output, path);
            _WriteByte((byte)':', output);
            _WriteValue(value, output, path);
            path.RemoveAt(path.Count - 1);
        }

        _WriteByte((byte)'}', output);
    }

    private static void _WriteArray(
        JsonElement element,
        IBufferWriter<byte> output,
        List<(string? Name, int Index)> path
    )
    {
        _WriteByte((byte)'[', output);

        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            if (index > 0)
            {
                _WriteByte((byte)',', output);
            }

            path.Add((null, index));
            _WriteValue(item, output, path);
            path.RemoveAt(path.Count - 1);
            index++;
        }

        _WriteByte((byte)']', output);
    }

    private static string _GetString(JsonElement element, List<(string? Name, int Index)> path)
    {
        try
        {
            return element.GetString()!;
        }
        catch (InvalidOperationException e)
        {
            throw _Error(path, "The string is not valid UTF-16.", e);
        }
    }

    private static void _WriteString(string value, IBufferWriter<byte> output, List<(string? Name, int Index)> path)
    {
        _WriteByte((byte)'"', output);

        var runStart = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c is >= (char)0x20 and not '"' and not '\\')
            {
                continue;
            }

            _WriteUtf8(value.AsSpan(runStart, i - runStart), output, path);
            runStart = i + 1;

            switch (c)
            {
                case '"':
                    _WriteRaw("\\\""u8, output);
                    break;
                case '\\':
                    _WriteRaw("\\\\"u8, output);
                    break;
                case '\b':
                    _WriteRaw("\\b"u8, output);
                    break;
                case '\t':
                    _WriteRaw("\\t"u8, output);
                    break;
                case '\n':
                    _WriteRaw("\\n"u8, output);
                    break;
                case '\f':
                    _WriteRaw("\\f"u8, output);
                    break;
                case '\r':
                    _WriteRaw("\\r"u8, output);
                    break;
                default:
                    var span = output.GetSpan(6);
                    "\\u00"u8.CopyTo(span);
                    span[4] = _LowerHex(c >> 4);
                    span[5] = _LowerHex(c & 0xF);
                    output.Advance(6);
                    break;
            }
        }

        _WriteUtf8(value.AsSpan(runStart), output, path);
        _WriteByte((byte)'"', output);
    }

    private static void _WriteUtf8(
        ReadOnlySpan<char> chars,
        IBufferWriter<byte> output,
        List<(string? Name, int Index)> path
    )
    {
        if (chars.IsEmpty)
        {
            return;
        }

        var span = output.GetSpan(_StrictUtf8.GetMaxByteCount(chars.Length));

        try
        {
            output.Advance(_StrictUtf8.GetBytes(chars, span));
        }
        catch (EncoderFallbackException e)
        {
            throw _Error(path, "The string contains a lone UTF-16 surrogate.", e);
        }
    }

    private static void _WriteNumber(
        ReadOnlySpan<byte> literal,
        IBufferWriter<byte> output,
        List<(string? Name, int Index)> path
    )
    {
        if (
            !double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value)
        )
        {
            throw _Error(path, $"The number {_Echo(literal)} is outside the IEEE-754 double range.");
        }

        // An integer literal above 2^53 - 1 is usually an identifier, and rounding it would give distinct identifiers
        // one hash. Fraction and exponent literals round to the nearest double, as RFC 8785 specifies.
        if (literal.IndexOfAny((byte)'.', (byte)'e', (byte)'E') < 0 && Math.Abs(value) > _MaxSafeInteger)
        {
            throw _Error(
                path,
                $"The integer {_Echo(literal)} is outside ±(2^53 - 1), where a double cannot hold every integer; send it as a JSON string."
            );
        }

        if (value == 0)
        {
            // Covers -0, which RFC 8785 writes as 0 like ECMAScript does.
            _WriteByte((byte)'0', output);

            return;
        }

        Span<byte> shortest = stackalloc byte[32];

        // "R" yields the shortest digit string that round-trips to the same double, the digits ECMAScript uses.
        var formatted = value.TryFormat(shortest, out var written, "R", CultureInfo.InvariantCulture);
        Debug.Assert(formatted);

        _WriteEcmaScriptNumber(shortest[..written], output);
    }

    /// <summary>
    /// Rewrites a finite, non-zero double formatted with the .NET "R" format, such as <c>-1.5E-07</c> or
    /// <c>123.45</c>, the way ECMAScript <c>Number.prototype.toString</c> writes it (ECMA-262, Number::toString).
    /// </summary>
    private static void _WriteEcmaScriptNumber(ReadOnlySpan<byte> roundTrip, IBufferWriter<byte> output)
    {
        var isNegative = roundTrip[0] == (byte)'-';
        var i = isNegative ? 1 : 0;

        // "R" writes at most 17 significant digits, plus up to four leading zeros before it switches to an exponent.
        Span<byte> digitBuffer = stackalloc byte[32];
        var digitCount = 0;
        var integerDigits = 0;

        for (; i < roundTrip.Length && char.IsAsciiDigit((char)roundTrip[i]); i++)
        {
            digitBuffer[digitCount++] = roundTrip[i];
            integerDigits++;
        }

        if (i < roundTrip.Length && roundTrip[i] == (byte)'.')
        {
            for (i++; i < roundTrip.Length && char.IsAsciiDigit((char)roundTrip[i]); i++)
            {
                digitBuffer[digitCount++] = roundTrip[i];
            }
        }

        var exponent = 0;

        if (i < roundTrip.Length && roundTrip[i] == (byte)'E')
        {
            var parsed = int.TryParse(
                roundTrip[(i + 1)..],
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out exponent
            );
            Debug.Assert(parsed);
        }

        // With leading and trailing zeros removed, the value is 0.digits × 10^n.
        ReadOnlySpan<byte> digits = digitBuffer[..digitCount];
        var leading = digits.IndexOfAnyExcept((byte)'0');
        var n = integerDigits - leading + exponent;
        digits = digits[leading..(digits.LastIndexOfAnyExcept((byte)'0') + 1)];
        var k = digits.Length;

        // The longest form is "-0.00000" followed by 17 digits: 25 bytes.
        var span = output.GetSpan(32);
        var length = 0;

        if (isNegative)
        {
            span[length++] = (byte)'-';
        }

        if (k <= n && n <= 21)
        {
            digits.CopyTo(span[length..]);
            length += k;
            span.Slice(length, n - k).Fill((byte)'0');
            length += n - k;
        }
        else if (n is > 0 and <= 21)
        {
            digits[..n].CopyTo(span[length..]);
            length += n;
            span[length++] = (byte)'.';
            digits[n..].CopyTo(span[length..]);
            length += k - n;
        }
        else if (n is > -6 and <= 0)
        {
            span[length++] = (byte)'0';
            span[length++] = (byte)'.';
            span.Slice(length, -n).Fill((byte)'0');
            length += -n;
            digits.CopyTo(span[length..]);
            length += k;
        }
        else
        {
            span[length++] = digits[0];

            if (k > 1)
            {
                span[length++] = (byte)'.';
                digits[1..].CopyTo(span[length..]);
                length += k - 1;
            }

            var e = n - 1;
            span[length++] = (byte)'e';
            span[length++] = e < 0 ? (byte)'-' : (byte)'+';
            var formattedExponent = Math.Abs(e)
                .TryFormat(span[length..], out var exponentLength, provider: CultureInfo.InvariantCulture);
            Debug.Assert(formattedExponent);
            length += exponentLength;
        }

        output.Advance(length);
    }

    private static void _WriteByte(byte value, IBufferWriter<byte> output)
    {
        output.GetSpan(1)[0] = value;
        output.Advance(1);
    }

    private static void _WriteRaw(ReadOnlySpan<byte> bytes, IBufferWriter<byte> output)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }

    private static byte _LowerHex(int nibble)
    {
        return (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
    }

    private static JsonException _Error(List<(string? Name, int Index)> path, string message, Exception? inner = null)
    {
        var builder = new StringBuilder("$");

        foreach (var (name, index) in path)
        {
            if (name is null)
            {
                builder.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            else
            {
                builder.Append("['").Append(_Echo(name)).Append("']");
            }
        }

        var jsonPath = builder.ToString();

        return new JsonException(
            $"Cannot canonicalize JSON at {jsonPath}: {message}",
            jsonPath,
            lineNumber: null,
            bytePositionInLine: null,
            inner
        );
    }

    private static string _Echo(string text)
    {
        if (text.Length <= _MaxEchoedLength)
        {
            return text;
        }

        // Cut before a high surrogate so the excerpt never ends in half a character.
        var cut = char.IsHighSurrogate(text[_MaxEchoedLength - 1]) ? _MaxEchoedLength - 1 : _MaxEchoedLength;

        return $"{text.AsSpan(0, cut)}…";
    }

    private static string _Echo(ReadOnlySpan<byte> numberLiteral)
    {
        // A number literal is ASCII, so any byte boundary is a character boundary.
        return numberLiteral.Length <= _MaxEchoedLength
            ? Encoding.ASCII.GetString(numberLiteral)
            : Encoding.ASCII.GetString(numberLiteral[.._MaxEchoedLength]) + "…";
    }
}
