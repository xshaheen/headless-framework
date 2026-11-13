// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Headless.Tus.Internal;

/// <summary>
/// What an upload is bound to: the endpoint it was created on, its owner, and its tenant when it has one. The
/// endpoint writes it on the upload's own record when the upload is created and compares it with the caller on
/// every later request.
/// </summary>
/// <remarks>
/// The stamp travels as entries of the upload's <c>Upload-Metadata</c> string under reserved keys, because every
/// store keeps that string as it is given and returns it unchanged, so the stamp needs nothing from a store. The
/// keys and the base64 form of their values are stored data: changing either orphans the uploads already written.
/// </remarks>
/// <param name="Endpoint">The name of the upload endpoint; the default endpoint's name is empty.</param>
/// <param name="OwnerKey">The owner key.</param>
/// <param name="TenantId">The tenant id, or <see langword="null"/> for an upload created with no tenant.</param>
internal sealed record TusUploadStamp(string Endpoint, string OwnerKey, string? TenantId)
{
    /// <summary>The key prefix only the server may use in an upload's metadata.</summary>
    public const string ReservedPrefix = "headless-";

    /// <summary>
    /// The longest owner key an upload can carry, in characters. Encoded, a key of this length still fits beside
    /// the client's own metadata in the stores that cap the whole string.
    /// </summary>
    public const int MaxOwnerKeyLength = 256;

    private const string _EndpointKey = ReservedPrefix + "endpoint";
    private const string _OwnerKey = ReservedPrefix + "owner";
    private const string _TenantKey = ReservedPrefix + "tenant";

    /// <summary>Whether <paramref name="key"/> is one only the server may use, in any letter case.</summary>
    public static bool IsReserved(ReadOnlySpan<char> key)
    {
        return key.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds the first key in the client's <paramref name="metadata"/> that only the server may use.</summary>
    public static bool TryFindReservedKey(string? metadata, [NotNullWhen(true)] out string? key)
    {
        foreach (var entry in _Entries(metadata))
        {
            if (IsReserved(_KeyOf(entry)))
            {
                key = _KeyOf(entry).ToString();

                return true;
            }
        }

        key = null;

        return false;
    }

    /// <summary>Returns <paramref name="metadata"/> followed by the stamp's entries.</summary>
    public string AppendTo(string? metadata)
    {
        var builder = new StringBuilder();

        foreach (var entry in _Entries(metadata))
        {
            _Append(builder, entry);
        }

        // The default endpoint's name is empty, and a metadata parser that requires a value would reject an entry
        // without one, so only a named endpoint is written down. No entry therefore means the default endpoint.
        if (Endpoint.Length > 0)
        {
            _Append(builder, _EndpointKey, Endpoint);
        }

        _Append(builder, _OwnerKey, OwnerKey);

        if (TenantId is not null)
        {
            _Append(builder, _TenantKey, TenantId);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns <paramref name="metadata"/> without the server's entries: what the client sent, or
    /// <see langword="null"/> when it sent nothing.
    /// </summary>
    public static string? Strip(string? metadata)
    {
        var builder = new StringBuilder();

        foreach (var entry in _Entries(metadata))
        {
            if (!IsReserved(_KeyOf(entry)))
            {
                _Append(builder, entry);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// Reads the stamp from an upload's stored <paramref name="metadata"/>. Returns <see langword="null"/> for an
    /// upload that carries no readable stamp, which nobody may reach.
    /// </summary>
    public static TusUploadStamp? Read(string? metadata)
    {
        var endpoint = string.Empty;
        string? ownerKey = null;
        string? tenantId = null;

        foreach (var entry in _Entries(metadata))
        {
            var key = _KeyOf(entry);

            if (key is not (_EndpointKey or _OwnerKey or _TenantKey))
            {
                continue;
            }

            // An entry that cannot be read must not weaken the stamp, for example by turning a tenant's upload
            // into one that has no tenant.
            if (_Decode(entry) is not { } value)
            {
                return null;
            }

            switch (key)
            {
                case _EndpointKey:
                    endpoint = value;
                    break;
                case _OwnerKey:
                    ownerKey = value;
                    break;
                default:
                    tenantId = value;
                    break;
            }
        }

        return ownerKey is null ? null : new TusUploadStamp(endpoint, ownerKey, tenantId);
    }

    /// <summary>
    /// The entries of an <c>Upload-Metadata</c> string, each a key and an optional base64 value. A trailing comma
    /// is legal in the header and leaves no entry behind.
    /// </summary>
    private static string[] _Entries(string? metadata)
    {
        return string.IsNullOrEmpty(metadata)
            ? []
            : metadata.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static ReadOnlySpan<char> _KeyOf(string entry)
    {
        var separator = entry.IndexOf(' ', StringComparison.Ordinal);

        return separator < 0 ? entry : entry.AsSpan(0, separator);
    }

    private static string? _Decode(string entry)
    {
        var encoded = entry.AsSpan(_KeyOf(entry).Length).Trim();
        var bytes = new byte[encoded.Length];

        return Convert.TryFromBase64Chars(encoded, bytes, out var written) && written > 0
            ? Encoding.UTF8.GetString(bytes, 0, written)
            : null;
    }

    private static void _Append(StringBuilder builder, string entry)
    {
        if (builder.Length > 0)
        {
            builder.Append(',');
        }

        builder.Append(entry);
    }

    private static void _Append(StringBuilder builder, string key, string value)
    {
        _Append(builder, key);
        builder.Append(' ').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
    }
}
