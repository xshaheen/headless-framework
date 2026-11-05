// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Dashboard;

/// <summary>
/// The dashboard shell's settings: where it is served, how it links to the host's tracing, which sources its Content
/// Security Policy adds, the host's contribution overrides, and how long it waits for each module.
/// </summary>
/// <remarks>
/// Set them through <see cref="HeadlessDashboardSetupBuilder"/> inside <c>AddHeadlessDashboard</c>. They are validated
/// when the host starts.
/// </remarks>
[PublicAPI]
public sealed class HeadlessDashboardOptions
{
    /// <summary>The default <see cref="BasePath"/>: <c>/headless</c>.</summary>
    public const string DefaultBasePath = "/headless";

    /// <summary>
    /// Gets or sets the path under the host's <c>PathBase</c> at which the dashboard is served. Defaults to
    /// <see cref="DefaultBasePath"/>. The root is refused, because the dashboard's client route would answer every
    /// unmatched GET in the host.
    /// </summary>
    public string BasePath { get; set; } = DefaultBasePath;

    /// <summary>
    /// Gets or sets the link to the host's tracing tool, with <c>{traceId}</c> where the W3C trace id goes, such as
    /// <c>https://apm.example/trace/{traceId}</c>. When <see langword="null"/> or empty, the dashboard offers no
    /// <c>Open trace</c> link.
    /// </summary>
    public string? TraceLinkTemplate { get; set; }

    /// <summary>
    /// Gets or sets the origins allowed to call the dashboard's API cross-origin with credentials. Empty, the default,
    /// allows none. Host authentication refuses any, because its session cookie never leaves the dashboard's origin.
    /// </summary>
    public IList<string> CorsOrigins { get; set; } = [];

    /// <summary>Gets or sets the sources the host adds to the dashboard's Content Security Policy.</summary>
    public DashboardContentSecurityPolicy ContentSecurityPolicy { get; set; } = new();

    /// <summary>Gets or sets the host's overrides of module contributions by id.</summary>
    public ContributionOverrides Contributions { get; set; } = new();

    /// <summary>
    /// Gets or sets how long Needs you, Overview, id lookup, and related objects wait for each module. A module that
    /// has not answered by then, or that fails, shows as <c>Stale</c> while the other modules' answers are still
    /// served. Defaults to 2 seconds; must be positive.
    /// </summary>
    public TimeSpan AggregationTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The authentication mode the builder chose, so validation can refuse settings that mode cannot honor.</summary>
    internal AuthMode AuthMode { get; set; }
}

/// <summary>Validates <see cref="HeadlessDashboardOptions"/> when the host starts.</summary>
internal sealed class HeadlessDashboardOptionsValidator : AbstractValidator<HeadlessDashboardOptions>
{
    public HeadlessDashboardOptionsValidator()
    {
        RuleFor(x => x.BasePath)
            .Must(path => !string.IsNullOrEmpty(path) && DashboardSpaHelper.NormalizeBasePath(path).Length > 0)
            .WithMessage(
                "BasePath cannot be the root path, because the dashboard's client route would answer every unmatched "
                    + "GET in the host. Use a path such as \"/headless\"."
            );

        RuleFor(x => x.CorsOrigins)
            .Empty()
            .When(x => x.AuthMode == AuthMode.Host)
            .WithMessage(
                "Host authentication serves the dashboard from the host's own origin only, because its session cookie "
                    + "never leaves that origin. Remove SetCorsOrigins, or use WithBasicAuth or WithApiKey."
            );

        RuleFor(x => x.TraceLinkTemplate)
            .Must(template => template!.Contains("{traceId}", StringComparison.Ordinal))
            .When(x => !string.IsNullOrEmpty(x.TraceLinkTemplate))
            .WithMessage("TraceLinkTemplate must contain {traceId} where the trace id goes.");

        RuleFor(x => x.AggregationTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("AggregationTimeout must be positive, such as 2 seconds.");

        RuleFor(x => x.ContentSecurityPolicy).NotNull();
        RuleFor(x => x.Contributions).NotNull();
    }
}
