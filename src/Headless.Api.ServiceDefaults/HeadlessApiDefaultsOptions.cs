// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Headless.Api.ServiceDefaults;

/// <summary>Options for <see cref="SetupApi.UseHeadless(WebApplication, Action{HeadlessApiDefaultsOptions}?)"/>.</summary>
/// <remarks>
/// Each <c>Use*</c> switch drops one <see cref="HeadlessPipelineStage"/>. <see cref="InsertBefore"/> and
/// <see cref="InsertAfter"/> add application middleware at a stage's position, so a host keeps the framework order
/// instead of rebuilding it by hand.
/// </remarks>
[PublicAPI]
public sealed class HeadlessApiDefaultsOptions
{
    internal const string AppliedKey = "Headless.Api.Defaults.Applied";

    private readonly List<PipelineInsertion> _insertions = [];

    /// <summary>Whether to run ASP.NET Core forwarded-headers middleware.</summary>
    public bool UseForwardedHeaders { get; set; } = true;

    /// <summary>
    /// Trusts forwarded headers from any proxy. Keep disabled unless the app is only reachable through trusted infrastructure.
    /// </summary>
    public bool TrustForwardedHeadersFromAnyProxy { get; set; }

    /// <summary>The forwarded headers to process.</summary>
    public ForwardedHeaders ForwardedHeaders { get; set; } =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

    /// <summary>
    /// Optional callback applied to <see cref="ForwardedHeadersOptions"/> before the forwarded-headers middleware
    /// is registered. Only invoked when <see cref="UseForwardedHeaders"/> is <see langword="true"/>.
    /// </summary>
    public Action<ForwardedHeadersOptions>? ConfigureForwardedHeaders { get; set; }

    /// <summary>Whether to run response compression middleware.</summary>
    public bool UseResponseCompression { get; set; } = true;

    /// <summary>Whether to convert empty error status codes into ProblemDetails when possible.</summary>
    public bool UseStatusCodePages { get; set; } = true;

    /// <summary>Whether to run ASP.NET Core exception-handler middleware.</summary>
    public bool UseExceptionHandler { get; set; } = true;

    /// <summary>
    /// The error endpoint path used by ASP.NET Core's exception-handler middleware. Leave unset to use ProblemDetails directly.
    /// </summary>
    [StringSyntax("Route")]
    public string? ExceptionHandlerPath { get; set; }

    /// <summary>Whether exception-handler middleware should create a scope for errors when a path is configured.</summary>
    public bool CreateScopeForErrors { get; set; } = true;

    /// <summary>
    /// Whether to run HTTPS redirection middleware. It never runs in the Development or Test environment, where the
    /// host usually serves plain HTTP and a redirect breaks local clients and in-memory test servers.
    /// </summary>
    public bool UseHttpsRedirection { get; set; } = true;

    /// <summary>
    /// Whether to run HSTS middleware. It never runs in the Development or Test environment, so a browser does not
    /// pin HTTPS for a local host name.
    /// </summary>
    public bool UseHsts { get; set; } = true;

    /// <summary>Whether to add a no-cache header when the response did not set cache headers.</summary>
    public bool SetNoCacheWhenMissingCacheHeaders { get; set; } = true;

    /// <summary>
    /// Adds application middleware immediately before <paramref name="stage"/>, so it runs outside that stage.
    /// </summary>
    /// <param name="stage">The stage to anchor on. The anchor holds even when the stage itself is turned off.</param>
    /// <param name="configure">Adds the middleware, for example <c>app =&gt; app.UseHttpLogging()</c>.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <remarks>Insertions at the same anchor run in the order they were added.</remarks>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="stage"/> is not a defined stage.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessApiDefaultsOptions InsertBefore(HeadlessPipelineStage stage, Action<IApplicationBuilder> configure)
    {
        return _Insert(stage, after: false, configure);
    }

    /// <summary>
    /// Adds application middleware immediately after <paramref name="stage"/>, so it runs inside that stage.
    /// </summary>
    /// <param name="stage">The stage to anchor on. The anchor holds even when the stage itself is turned off.</param>
    /// <param name="configure">Adds the middleware, for example <c>app =&gt; app.UseHttpLogging()</c>.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <remarks>Insertions at the same anchor run in the order they were added.</remarks>
    /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="stage"/> is not a defined stage.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessApiDefaultsOptions InsertAfter(HeadlessPipelineStage stage, Action<IApplicationBuilder> configure)
    {
        return _Insert(stage, after: true, configure);
    }

    internal void ApplyInsertions(HeadlessPipelineStage stage, bool after, IApplicationBuilder app)
    {
        foreach (var insertion in _insertions)
        {
            if (insertion.Stage == stage && insertion.After == after)
            {
                insertion.Configure(app);
            }
        }
    }

    private HeadlessApiDefaultsOptions _Insert(
        HeadlessPipelineStage stage,
        bool after,
        Action<IApplicationBuilder> configure
    )
    {
        Argument.IsInEnum(stage);
        Argument.IsNotNull(configure);

        _insertions.Add(new PipelineInsertion(stage, after, configure));

        return this;
    }

    private sealed record PipelineInsertion(
        HeadlessPipelineStage Stage,
        bool After,
        Action<IApplicationBuilder> Configure
    );
}
