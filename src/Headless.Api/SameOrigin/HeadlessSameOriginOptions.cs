// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Api;

/// <summary>
/// Configures the same-origin guard that <c>RequireSameOrigin()</c> and <see cref="RequireSameOriginAttribute"/> apply
/// to an endpoint, and that a JWT read from a cookie must also pass.
/// </summary>
/// <remarks>
/// A browser attaches cookies to a request whatever site started it, so an endpoint that trusts a cookie must also
/// prove the request came from the API's own pages. The guard reads the browser-set <c>Origin</c> and
/// <c>Sec-Fetch-Site</c> headers and refuses the request unless they show the API's own origin, or a listed origin on
/// the same site. A request with neither header is refused, so a non-browser client calling a guarded endpoint must
/// send <c>Origin</c>. The guard compares against the request's scheme and host, so behind a reverse proxy run the
/// forwarded-headers middleware first.
/// </remarks>
[PublicAPI]
public sealed class HeadlessSameOriginOptions
{
    /// <summary>
    /// Origins besides the API's own that may call a guarded endpoint, such as a single-page app on
    /// <c>https://app.example.com</c> calling <c>https://api.example.com</c>. Each is an absolute <c>http</c> or
    /// <c>https</c> origin with no path, trailing slash, query, or default port, because the browser's <c>Origin</c>
    /// header never carries them. A listed origin passes only when the browser reports the request as
    /// <c>same-site</c>: a request from another site is refused even when its origin is listed.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// A request header that every guarded request must carry with a non-empty value, such as
    /// <c>X-Requested-With</c>. A cross-origin page cannot add a custom header without a CORS preflight the API
    /// refuses, so requiring one adds a check that does not depend on the browser sending fetch metadata. Default
    /// <see langword="null"/>: no header is required.
    /// </summary>
    public string? RequiredHeaderName { get; set; }
}

internal sealed class HeadlessSameOriginOptionsValidator : AbstractValidator<HeadlessSameOriginOptions>
{
    public HeadlessSameOriginOptionsValidator()
    {
        RuleForEach(x => x.AllowedOrigins).Custom(_ValidateOrigin);

        RuleFor(x => x.RequiredHeaderName)
            .Must(HttpSyntax.IsToken)
            .When(x => x.RequiredHeaderName is not null)
            .WithMessage(
                (_, name) =>
                    $"{nameof(HeadlessSameOriginOptions.RequiredHeaderName)} '{name}' is not a valid HTTP token: only"
                    + " letters, digits, and !#$%&'*+-.^_`|~ are allowed."
            );
    }

    private static void _ValidateOrigin(string? origin, ValidationContext<HeadlessSameOriginOptions> context)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            context.AddFailure("An allowed origin must not be empty.");
            return;
        }

        var problem = HttpSyntax.DescribeOriginProblem(origin);

        if (problem is not null)
        {
            context.AddFailure($"Allowed origin '{origin}' {problem}");
            return;
        }

        // The guard protects browser sessions, and only web pages send fetch metadata, so a hybrid-app scheme such as
        // 'capacitor://' would never reach the same-site rule an allowed origin needs.
        if (
            !origin.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase)
            && !origin.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase)
        )
        {
            context.AddFailure($"Allowed origin '{origin}' must use the http or https scheme.");
        }
    }
}
