// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.RegularExpressions;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Tenant identifier source reading the request host: matches <c>Request.Host.Host</c> — the
/// post-forwarding host, never <c>X-Forwarded-Host</c> directly — against the configured
/// templates and yields the <c>{tenant}</c> capture.
/// </summary>
/// <remarks>
/// <para>
/// Input guards run before any matcher so a pathological or non-domain host never enters the regex:
/// a host over 253 characters (the DNS limit) and IPv4/IPv6 literals yield
/// <see cref="TenantIdentifierSourceResult.None"/>. One trailing dot is stripped (FQDN form) and
/// matching is case-insensitive; the capture keeps the input's casing because sources never shape
/// identifiers — <c>ITenantCatalogService</c> owns normalization.
/// </para>
/// <para>
/// The compiled templates are non-backtracking, so a match timeout is practically
/// unreachable; should one still occur it maps to <see cref="TenantIdentifierSourceResult.Invalid"/>
/// plus a once-per-process warning that names the operator-supplied template only — never the host.
/// </para>
/// <para>
/// <paramref name="matchTimeout"/> and <paramref name="maxHostLength"/> are test seams for forcing
/// the timeout path, which the production guard combination (253-character cap plus the
/// non-backtracking engine) makes unreachable through <see cref="GetIdentifier"/>; production
/// construction takes the defaults (<see cref="Headless.Constants.RegexPatterns.MatchTimeout"/> and
/// the DNS limit).
/// </para>
/// </remarks>
internal sealed partial class HostTenantIdentifierSource(
    IOptions<HostTenantIdentifierSourceOptions> options,
    ILogger<HostTenantIdentifierSource> logger,
    TimeSpan? matchTimeout = null,
    // 253 = the DNS limit on a fully qualified host name; a longer host is not a tenant address,
    // and refusing it before the matcher keeps the regex input bounded. Parameter default
    // because primary-constructor defaults cannot reference class constants.
    int maxHostLength = 253
) : ITenantIdentifierSource
{
    // Fires exactly once per process for HEADLESS_TENANT_HOST_TEMPLATE_TIMEOUT. 0 = not yet warned,
    // 1 = warned. CompareExchange ensures the warning is emitted by at most one request, so a
    // hostile workload cannot flood the log.
    private static int _templateTimeoutWarningEmitted;

    private readonly int _maxHostLength = maxHostLength;

    // Compiled once at construction: startup validation proved every template parses, so a
    // failure here is a programming/test error, not operator input — fail loudly.
    private readonly (string Template, HostTemplate Compiled)[] _templates = _CompileTemplates(
        options.Value.Templates,
        matchTimeout
    );

    /// <inheritdoc/>
    public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
    {
        Argument.IsNotNull(context);

        var host = context.Request.Host;

        if (!host.HasValue)
        {
            return TenantIdentifierSourceResult.None;
        }

        var hostname = host.Host;

        if (hostname.EndsWith('.'))
        {
            hostname = hostname[..^1];
        }

        if (hostname.Length > _maxHostLength || IPAddress.TryParse(hostname, out _))
        {
            return TenantIdentifierSourceResult.None;
        }

        foreach (var (template, compiled) in _templates)
        {
            try
            {
                if (compiled.Match(hostname, out var identifier))
                {
                    return TenantIdentifierSourceResult.Found(identifier);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _WarnTemplateTimeoutOnce(template);
                return TenantIdentifierSourceResult.Invalid;
            }
        }

        return TenantIdentifierSourceResult.None;
    }

    /// <summary>
    /// Test seam: resets the once-per-process template-timeout warning flag so tests asserting on
    /// the HEADLESS_TENANT_HOST_TEMPLATE_TIMEOUT log entry can run in any order.
    /// </summary>
    internal static void ResetTemplateTimeoutWarningForTesting()
    {
        Volatile.Write(ref _templateTimeoutWarningEmitted, 0);
    }

    private static (string Template, HostTemplate Compiled)[] _CompileTemplates(
        IList<string> templates,
        TimeSpan? matchTimeout
    )
    {
        var compiledTemplates = new (string Template, HostTemplate Compiled)[templates.Count];

        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];

            if (!HostTemplate.TryParse(template, out var compiled, out var error, matchTimeout))
            {
                throw new InvalidOperationException(
                    $"{nameof(HostTenantIdentifierSource)} was constructed with an invalid template: {error}"
                );
            }

            compiledTemplates[i] = (template, compiled!);
        }

        return compiledTemplates;
    }

    private void _WarnTemplateTimeoutOnce(string template)
    {
        if (Volatile.Read(ref _templateTimeoutWarningEmitted) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _templateTimeoutWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        LogTemplateTimeoutWarning(logger, template);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "HEADLESS_TENANT_HOST_TEMPLATE_TIMEOUT",
        Level = LogLevel.Warning,
        Message = "A host tenant identifier source template exceeded its match timeout and the request "
            + "was rejected as invalid. The non-backtracking compiled templates make this practically "
            + "unreachable; if it recurs, shorten or split the template. Template: '{Template}'. "
            + "This warning is emitted once per process and never logs the request host."
    )]
    private static partial void LogTemplateTimeoutWarning(ILogger logger, string template);
}
