// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using Headless.Checks;
using Headless.Fencing;

namespace Headless.Leases;

/// <summary>
/// The opaque cursor of the fenced-lease list: <c>v1.</c> and the base64url JSON triple
/// <c>[tenantId | null, kind, resource]</c> of the last lease of the previous page, as the inspector's keyset needs it.
/// </summary>
internal static class FencedLeaseCursor
{
    private const string _Prefix = "v1.";

    public static string Encode(FencedLeasePosition position)
    {
        Argument.IsNotNull(position);

        var json = JsonSerializer.SerializeToUtf8Bytes(
            [position.TenantId, position.Kind, position.Resource],
            LeasesDashboardJsonContext.Default.StringArray
        );

        return _Prefix + Base64Url.EncodeToString(json);
    }

    /// <summary>Reads a cursor <see cref="Encode"/> wrote; any other text is not a cursor.</summary>
    public static bool TryDecode(string value, [NotNullWhen(true)] out FencedLeasePosition? position)
    {
        position = null;

        if (!value.StartsWith(_Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var json = Base64Url.DecodeFromChars(value.AsSpan(_Prefix.Length));
            var parts = JsonSerializer.Deserialize(json, LeasesDashboardJsonContext.Default.StringArray);

            if (parts is not [var tenantId, { Length: > 0 } kind, { Length: > 0 } resource])
            {
                return false;
            }

            position = new FencedLeasePosition(tenantId, kind, resource);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
