// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;

namespace Headless.Urls;

/// <summary>
/// Represents a query parameter value with the ability to track whether it was already encoded when created.
/// </summary>
internal readonly struct QueryParamValue
{
    private readonly string? _encodedValue;

    public QueryParamValue(object? value, bool isEncoded)
    {
        if (isEncoded && value is string s)
        {
            _encodedValue = s;
            Value = Url.Decode(s, interpretPlusAsSpace: true);
        }
        else
        {
            Value = value;
            _encodedValue = null;
        }
    }

    public object? Value { get; }

    public string? Encode(bool encodeSpaceAsPlus)
    {
        return Value is null
            ? null
            : _encodedValue
                ?? (
                    Value is string s
                        ? Url.Encode(s, encodeSpaceAsPlus)
                        : Url.Encode(Value.ToInvariantString(), encodeSpaceAsPlus)
                );
    }
}
