// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Tenant identifier source reading one or more request headers: exactly one value across the
/// configured <see cref="HeaderTenantIdentifierSourceOptions.HeaderNames"/> is yielded raw and
/// unchanged; an absent or whitespace-only value is <see cref="TenantIdentifierSourceResult.None"/>;
/// any second non-blank value is <see cref="TenantIdentifierSourceResult.Invalid"/>.
/// </summary>
/// <remarks>
/// <para>
/// Ambiguity is counted, not compared: a header line repeated with the same value is as
/// ambiguous as two differing lines, and a second configured name present beside the first counts
/// the same way. Blank lines count as absent, so a blank line beside one real value does not make it
/// ambiguous, and a name listed more than once (case-insensitively) is read once. One header line
/// whose value contains a comma is a single value — the source never
/// splits on commas — so <c>a,b</c> reaches the catalog unchanged, where it can only ever match a
/// tenant whose identifier literally contains a comma.
/// </para>
/// <para>
/// Every consult, whatever its outcome, appends each configured name to the response <c>Vary</c>
/// header (without duplicating an entry already present, case-insensitively). A response resolved by
/// a later source — or served as host context — and cached without <c>Vary</c> would otherwise be
/// served to a subsequent request carrying a different header value. The header is stamped
/// <em>before</em> the request is read so a rejection response carries it too.
/// </para>
/// <para>
/// No trimming, lowercasing, or shape validation happens here; <c>ITenantCatalogService</c> owns
/// normalization. A header can only ever select an existing enabled tenant through the catalog,
/// and identifier/claim mismatch enforcement still rejects an authenticated caller whose tenant claim disagrees; what it does bypass is
/// any perimeter control bound to a tenant's hostname.
/// </para>
/// </remarks>
internal sealed class HeaderTenantIdentifierSource(IOptions<HeaderTenantIdentifierSourceOptions> options)
    : ITenantIdentifierSource
{
    // Snapshotted once: startup validation proved the list is non-empty and every name is a token.
    // Distinct because the same name can reach the list through several registration paths (a repeated
    // AddHeaderSource(string), a configuration bind, two binds of one section); that is benign intent,
    // not an operator error, and reading one header twice must not count its single value as ambiguous.
    private readonly string[] _headerNames = [.. options.Value.HeaderNames.Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc/>
    public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
    {
        Argument.IsNotNull(context);

        _AppendVary(context.Response);

        var count = 0;
        string? identifier = null;

        foreach (var headerName in _headerNames)
        {
            if (!context.Request.Headers.TryGetValue(headerName, out var values))
            {
                continue;
            }

            foreach (var value in values)
            {
                // Blank equals absent, so a blank line must not make a lone value elsewhere ambiguous.
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (++count > 1)
                {
                    return TenantIdentifierSourceResult.Invalid;
                }

                identifier = value;
            }
        }

        return count == 1 ? TenantIdentifierSourceResult.Found(identifier) : TenantIdentifierSourceResult.None;
    }

    private void _AppendVary(HttpResponse response)
    {
        // Headers are frozen once the response has started; a consult that late (never from the
        // catalog middleware, which runs before next()) cannot vary the response anyway.
        if (response.HasStarted)
        {
            return;
        }

        var headers = response.Headers;

        foreach (var headerName in _headerNames)
        {
            if (!_ContainsToken(headers.Vary, headerName))
            {
                headers.Append(HeaderNames.Vary, headerName);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="vary"/> already lists <paramref name="token"/>, whether as its own
    /// entry or inside a comma-joined line a consumer stamped earlier (header names are case-insensitive).
    /// </summary>
    private static bool _ContainsToken(StringValues vary, string token)
    {
        foreach (var entry in vary)
        {
            if (entry is null)
            {
                continue;
            }

            var span = entry.AsSpan();

            foreach (var range in span.Split(','))
            {
                if (span[range].Trim().Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
