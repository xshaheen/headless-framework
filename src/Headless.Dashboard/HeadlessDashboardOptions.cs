// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Dashboard;

/// <summary>
/// The dashboard shell's settings: where it is served, how it links to the host's tracing, which sources its Content
/// Security Policy adds, the host's contribution overrides, how long it waits for each module, and how often its live
/// stream reads changes and sends heartbeats.
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

    /// <summary>
    /// Gets or sets how often each node reads every module's changes from the module's store while the node has a
    /// live stream open, so a change made through another node reaches this node's streams within this interval plus
    /// one second. Each read is one query per module per node, and none runs while no stream is open. Defaults to
    /// 2 seconds; must be positive.
    /// </summary>
    public TimeSpan ChangePollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how often an open live stream sends a <c>heartbeat</c> event. The page marks the stream
    /// <c>Stale</c> after 5 seconds without an event, so the interval must be shorter than that. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

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

        RuleFor(x => x.TraceLinkTemplate)
            .Must(template => template!.Contains("{traceId}", StringComparison.Ordinal))
            .When(x => !string.IsNullOrEmpty(x.TraceLinkTemplate))
            .WithMessage("TraceLinkTemplate must contain {traceId} where the trace id goes.");

        RuleFor(x => x.AggregationTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("AggregationTimeout must be positive, such as 2 seconds.");

        RuleFor(x => x.ChangePollInterval)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("ChangePollInterval must be positive, such as 2 seconds.");

        const string heartbeatMessage =
            "HeartbeatInterval must be positive and shorter than 5 seconds, because the page marks a stream that "
            + "sends nothing for 5 seconds as Stale. Use 2 seconds.";

        RuleFor(x => x.HeartbeatInterval)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage(heartbeatMessage)
            .LessThan(TimeSpan.FromSeconds(5))
            .WithMessage(heartbeatMessage);

        RuleFor(x => x.ContentSecurityPolicy).NotNull();
        RuleFor(x => x.Contributions).NotNull();
    }
}
