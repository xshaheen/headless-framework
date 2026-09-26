// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace Headless.Api.Cors;

/// <summary>
/// Configures one named CORS policy registered by <c>AddHeadlessCors</c> or <c>AddHeadlessAllowAnyCors</c>.
/// </summary>
/// <remarks>
/// A policy either names the origins it allows (<see cref="AllowedOrigins"/>, <see cref="AllowedOriginTemplates"/>,
/// or a registered <see cref="ICorsOriginSource"/>) or sets <see cref="AllowAnyOrigin"/>; startup fails when it does
/// neither. CORS governs browsers only: a native client, such as a React Native app on iOS or Android, sends no
/// <c>Origin</c> header, so no policy applies to it and its requests pass through unchanged.
/// </remarks>
[PublicAPI]
public sealed class HeadlessCorsOptions
{
    /// <summary>
    /// Exact origins allowed to call the API, such as <c>https://app.example.com</c>, <c>http://localhost:8081</c>
    /// (an Expo web dev server), or a hybrid mobile webview origin such as <c>capacitor://localhost</c>. Each is a
    /// scheme, host, and optional port, with no path, trailing slash, query, fragment, or user info, because the
    /// browser's <c>Origin</c> header never carries them and an entry that does silently matches nothing.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Origin templates with a leading wildcard label, such as <c>https://*.example.com</c>. A template matches any
    /// subdomain at any depth under the literal suffix, with the same scheme and port; it does not match the bare
    /// suffix itself, so list <c>https://example.com</c> in <see cref="AllowedOrigins"/> when that is allowed too.
    /// The suffix needs at least two labels, so <c>https://*</c> and <c>https://*.com</c> are rejected. A two-label
    /// public suffix such as <c>co.uk</c> still passes and admits every site under it; do not configure one.
    /// </summary>
    public List<string> AllowedOriginTemplates { get; set; } = [];

    /// <summary>
    /// Allows every origin. Credentials are then never allowed, and <see cref="AllowedOrigins"/>,
    /// <see cref="AllowedOriginTemplates"/>, and an <see cref="ICorsOriginSource"/> must not be configured. Startup
    /// fails in the Production environment unless <see cref="AllowAnyOriginInProduction"/> is also true.
    /// </summary>
    public bool AllowAnyOrigin { get; set; }

    /// <summary>
    /// Confirms that an <see cref="AllowAnyOrigin"/> policy is meant to run in Production, such as a public API that
    /// any site may call without credentials. Default <see langword="false"/>, so a development policy left in a
    /// production pipeline fails at startup instead of silently opening the API to every site.
    /// </summary>
    public bool AllowAnyOriginInProduction { get; set; }

    /// <summary>
    /// Whether browsers may send cookies and HTTP authentication on cross-origin requests and read the response.
    /// Default <see langword="false"/>. Enable it only for origins you trust with the user's session.
    /// </summary>
    public bool AllowCredentials { get; set; }

    /// <summary>
    /// Request headers allowed on cross-origin requests. Empty, the default, allows any header.
    /// </summary>
    public List<string> AllowedHeaders { get; set; } = [];

    /// <summary>
    /// HTTP methods allowed on cross-origin requests. Empty, the default, allows any method.
    /// </summary>
    public List<string> AllowedMethods { get; set; } = [];

    /// <summary>Response headers, beyond the CORS-safelisted ones, that browser scripts may read.</summary>
    public List<string> ExposedHeaders { get; set; } = [];

    /// <summary>
    /// How long a browser may cache a preflight response, sent as <c>Access-Control-Max-Age</c>.
    /// <see langword="null"/>, the default, sends no header and leaves the browser default in place. Browsers cap
    /// the value (Chromium at two hours), so a longer one has no further effect.
    /// </summary>
    public TimeSpan? MaxAge { get; set; }

    /// <summary>Marked by <c>AddHeadlessCorsOriginSource</c>; not bindable from configuration.</summary>
    internal bool HasOriginSource { get; set; }
}

internal sealed class HeadlessCorsOptionsValidator : AbstractValidator<HeadlessCorsOptions>
{
    public HeadlessCorsOptionsValidator()
        : this(environment: null) { }

    public HeadlessCorsOptionsValidator(IHostEnvironment? environment)
    {
        RuleFor(x => x)
            .Must(x =>
                x.AllowAnyOrigin
                || x.HasOriginSource
                || x.AllowedOrigins.Count > 0
                || x.AllowedOriginTemplates.Count > 0
            )
            .WithName(nameof(HeadlessCorsOptions.AllowedOrigins))
            .WithMessage(
                $"A CORS policy needs at least one entry in {nameof(HeadlessCorsOptions.AllowedOrigins)} or"
                    + $" {nameof(HeadlessCorsOptions.AllowedOriginTemplates)}, a registered origin source, or"
                    + $" {nameof(HeadlessCorsOptions.AllowAnyOrigin)}. For a development policy that allows any"
                    + " origin, use AddHeadlessAllowAnyCors."
            );

        When(x => x.AllowAnyOrigin, () => _AddAnyOriginRules(environment));

        RuleForEach(x => x.AllowedOrigins).Custom(_ValidateOrigin);
        RuleForEach(x => x.AllowedOriginTemplates).Custom(_ValidateTemplate);
        RuleForEach(x => x.AllowedHeaders).NotEmpty();
        RuleForEach(x => x.AllowedMethods).NotEmpty();
        RuleForEach(x => x.ExposedHeaders).NotEmpty();
        RuleFor(x => x.MaxAge).GreaterThan(TimeSpan.Zero).When(x => x.MaxAge is not null);
    }

    private void _AddAnyOriginRules(IHostEnvironment? environment)
    {
        RuleFor(x => x)
            .Must(x => x.AllowedOrigins.Count == 0 && x.AllowedOriginTemplates.Count == 0 && !x.HasOriginSource)
            .WithName(nameof(HeadlessCorsOptions.AllowAnyOrigin))
            .WithMessage(
                $"{nameof(HeadlessCorsOptions.AllowAnyOrigin)} already admits every origin; remove"
                    + $" {nameof(HeadlessCorsOptions.AllowedOrigins)}, {nameof(HeadlessCorsOptions.AllowedOriginTemplates)},"
                    + " and any origin source, or turn it off to restrict the policy."
            );

        // Browsers refuse a wildcard origin with credentials, and reflecting every origin instead would hand any
        // site the user's session.
        RuleFor(x => x.AllowCredentials)
            .Equal(toCompare: false)
            .WithMessage(
                $"{nameof(HeadlessCorsOptions.AllowAnyOrigin)} cannot be combined with"
                    + $" {nameof(HeadlessCorsOptions.AllowCredentials)}; list the trusted origins instead."
            );

        if (environment?.IsProduction() == true)
        {
            RuleFor(x => x.AllowAnyOriginInProduction)
                .Equal(toCompare: true)
                .WithMessage(
                    $"An {nameof(HeadlessCorsOptions.AllowAnyOrigin)} policy is registered in Production. Turn on"
                        + $" {nameof(HeadlessCorsOptions.AllowAnyOriginInProduction)} for a public API that any site"
                        + " may call, or register the policy only in development."
                );
        }
    }

    private static void _ValidateOrigin(string? origin, ValidationContext<HeadlessCorsOptions> context)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            context.AddFailure("An allowed origin must not be empty.");
            return;
        }

        if (origin.Contains('*', StringComparison.Ordinal))
        {
            context.AddFailure(
                $"Allowed origin '{origin}' contains a wildcard. Put wildcard subdomains in"
                    + $" {nameof(HeadlessCorsOptions.AllowedOriginTemplates)}, and use"
                    + $" {nameof(HeadlessCorsOptions.AllowAnyOrigin)} to allow any origin."
            );
            return;
        }

        var problem = _DescribeOriginProblem(origin);

        if (problem is not null)
        {
            context.AddFailure($"Allowed origin '{origin}' {problem}");
        }
    }

    private static void _ValidateTemplate(string? template, ValidationContext<HeadlessCorsOptions> context)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            context.AddFailure("An allowed origin template must not be empty.");
            return;
        }

        var schemeEnd = template.IndexOf("://", StringComparison.Ordinal);

        if (schemeEnd < 0 || !template.AsSpan(schemeEnd + 3).StartsWith("*.", StringComparison.Ordinal))
        {
            context.AddFailure(
                $"Allowed origin template '{template}' must start with a scheme followed by '*.', such as"
                    + " 'https://*.example.com'."
            );
            return;
        }

        var suffix = template[(schemeEnd + 5)..];

        if (suffix.Contains('*', StringComparison.Ordinal))
        {
            context.AddFailure($"Allowed origin template '{template}' may contain only one leading wildcard.");
            return;
        }

        // Validate the suffix as a concrete origin, so the template obeys every rule an exact origin does.
        var concrete = string.Concat(template.AsSpan(0, schemeEnd + 3), suffix);
        var problem = _DescribeOriginProblem(concrete);

        if (problem is not null)
        {
            context.AddFailure($"Allowed origin template '{template}' {problem}");
            return;
        }

        // A one-label suffix ('*.com') or none at all ('*.') matches every host under a top-level domain.
        var host = new Uri(concrete).IdnHost;

        if (!host.Contains('.', StringComparison.Ordinal) || host.EndsWith('.'))
        {
            context.AddFailure(
                $"Allowed origin template '{template}' needs a literal suffix of at least two labels after '*.',"
                    + " such as 'https://*.example.com'."
            );
        }
    }

    /// <summary>Returns why <paramref name="origin"/> is not a bare serialized origin, or null when it is one.</summary>
    private static string? _DescribeOriginProblem(string origin)
    {
        // Uri silently trims surrounding whitespace, which would hide a copy-paste error in configuration.
        if (origin.AsSpan().Trim().Length != origin.Length)
        {
            return "must not contain leading or trailing whitespace.";
        }

        // Any scheme with a host is a real origin: hybrid mobile webviews send 'capacitor://localhost' or
        // 'ionic://localhost'. A file URL serializes to the opaque origin 'null', so it can never match.
        if (
            !origin.Contains("://", StringComparison.Ordinal)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.Ordinal)
        )
        {
            return "must be an absolute origin with a scheme and host, such as 'https://app.example.com'.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "must not contain user info.";
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "must not contain a query or fragment.";
        }

        // Uri normalizes an empty path to '/', so read the raw string: 'https://a.com/' and 'https://a.com/x' both
        // fail, since the browser's Origin header never carries a path.
        var authorityStart = origin.IndexOf("://", StringComparison.Ordinal) + 3;

        if (origin.IndexOf('/', authorityStart) >= 0)
        {
            return "must not contain a path or trailing slash.";
        }

        return null;
    }
}
