// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Api.Cors;

/// <summary>
/// Configures the <see cref="HeadlessCorsConstants.RestrictedCors"/> policy registered by <c>AddHeadlessCors</c>.
/// </summary>
/// <remarks>
/// The policy is restricted by construction: it names at least one origin or origin template, and startup fails
/// otherwise. For a development-only policy that allows any origin, use
/// <see cref="HeadlessCorsConstants.AllowAnyCors"/>, which never allows credentials.
/// </remarks>
[PublicAPI]
public sealed class HeadlessCorsOptions
{
    /// <summary>
    /// Exact origins allowed to call the API, such as <c>https://app.example.com</c> or
    /// <c>http://localhost:5173</c>. Each is a scheme, host, and optional port, with no path, trailing slash,
    /// query, fragment, or user info, because the browser's <c>Origin</c> header never carries them and an entry
    /// that does silently matches nothing.
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
}

internal sealed class HeadlessCorsOptionsValidator : AbstractValidator<HeadlessCorsOptions>
{
    public HeadlessCorsOptionsValidator()
    {
        RuleFor(x => x)
            .Must(x => x.AllowedOrigins.Count > 0 || x.AllowedOriginTemplates.Count > 0)
            .WithName(nameof(HeadlessCorsOptions.AllowedOrigins))
            .WithMessage(
                $"The restricted CORS policy needs at least one entry in {nameof(HeadlessCorsOptions.AllowedOrigins)}"
                    + $" or {nameof(HeadlessCorsOptions.AllowedOriginTemplates)}. For a development policy that"
                    + $" allows any origin, use {nameof(HeadlessCorsConstants)}.{nameof(HeadlessCorsConstants.AllowAnyCors)}."
            );

        RuleForEach(x => x.AllowedOrigins).Custom(_ValidateOrigin);
        RuleForEach(x => x.AllowedOriginTemplates).Custom(_ValidateTemplate);
        RuleForEach(x => x.AllowedHeaders).NotEmpty();
        RuleForEach(x => x.AllowedMethods).NotEmpty();
        RuleForEach(x => x.ExposedHeaders).NotEmpty();
        RuleFor(x => x.MaxAge).GreaterThan(TimeSpan.Zero).When(x => x.MaxAge is not null);
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
                    + $" {nameof(HeadlessCorsConstants)}.{nameof(HeadlessCorsConstants.AllowAnyCors)} to allow any origin."
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

        if (
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            )
        )
        {
            return "must be an absolute http or https origin, such as 'https://app.example.com'.";
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
