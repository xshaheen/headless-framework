// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Leases;

/// <summary>
/// The dashboard id of a fenced lease: <c>{tenant}/{kind}/{resource}</c>, each segment escaped, with the empty first
/// segment for the host scope. The fencing store refuses an empty tenant id, so an empty segment can only mean the host
/// scope. The server mints every id; the browser only passes one back.
/// </summary>
internal static class FencedLeaseId
{
    public static string Format(string? tenantId, string kind, string resource) =>
        string.Concat(
            Uri.EscapeDataString(tenantId ?? string.Empty),
            "/",
            Uri.EscapeDataString(kind),
            "/",
            Uri.EscapeDataString(resource)
        );

    /// <summary>
    /// Reads an id <see cref="Format"/> wrote. Any other shape, or an empty kind or resource, is not a lease id.
    /// </summary>
    public static bool TryParse(
        string? id,
        out string? tenantId,
        [NotNullWhen(true)] out string? kind,
        [NotNullWhen(true)] out string? resource
    )
    {
        tenantId = null;
        kind = null;
        resource = null;

        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var segments = id.Split('/');

        if (segments.Length != 3 || segments[1].Length == 0 || segments[2].Length == 0)
        {
            return false;
        }

        tenantId = segments[0].Length == 0 ? null : Uri.UnescapeDataString(segments[0]);
        kind = Uri.UnescapeDataString(segments[1]);
        resource = Uri.UnescapeDataString(segments[2]);

        return true;
    }
}
