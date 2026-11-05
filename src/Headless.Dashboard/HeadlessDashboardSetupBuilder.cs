// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Fluent builder passed to the <c>AddHeadlessDashboard</c> delegate: chooses how operators sign in, where the
/// dashboard is served, and which modules it shows.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="WithHostAuthentication()"/>, <see cref="WithBasicAuth"/>, <see cref="WithApiKey"/>, or
/// <see cref="WithNoAuth"/> must be called; without one, <c>AddHeadlessDashboard</c> throws, so a dashboard never ships
/// unauthenticated by omission. Module packages add themselves through their own <c>Add{Module}</c> extensions on this
/// builder, which call <see cref="AddModule{TModule, TOptions}"/>.
/// </remarks>
[PublicAPI]
public sealed partial class HeadlessDashboardSetupBuilder
{
    private static readonly FrozenSet<string> _ReservedModuleIds = FrozenSet.Create(
        StringComparer.Ordinal,
        "api",
        "auth",
        "m",
        // The shell's own pages share the route space with module ids.
        "needs-you",
        "sign-in",
        "shell"
    );

    private readonly List<Action<DashboardContentSecurityPolicy>> _contentSecurityPolicyActions = [];
    private readonly List<Action<ContributionOverrides>> _contributionActions = [];
    private readonly List<IConfiguration> _contributionSections = [];
    private string _basePath = HeadlessDashboardOptions.DefaultBasePath;
    private string? _traceLinkTemplate;
    private string[] _corsOrigins = [];

    internal HeadlessDashboardSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    /// <summary>
    /// Gets the host's service collection, so a module package's <c>Add{Module}</c> extension registers the module's
    /// own services and options beside it.
    /// </summary>
    public IServiceCollection Services { get; }

    internal AuthConfig Auth { get; } = new();

    internal bool AuthConfigured { get; private set; }

    internal List<DashboardModuleDescriptor> Modules { get; } = [];

    /// <summary>
    /// Signs operators in through the host application with the host's default policy for viewing and operating. An
    /// operator signs in once, through <c>{base}/sign-in</c> or by posting the host's own credential to
    /// <c>{base}/api/auth/session</c>, and the dashboard then authenticates with its own session cookie alone.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public HeadlessDashboardSetupBuilder WithHostAuthentication()
    {
        return WithHostAuthentication(static _ => { });
    }

    /// <summary>
    /// Signs operators in through the host application. An operator signs in once, through <c>{base}/sign-in</c> or by
    /// posting the host's own credential to <c>{base}/api/auth/session</c>, and the dashboard then authenticates with
    /// its own session cookie alone.
    /// </summary>
    /// <remarks>
    /// The session cookie is <c>SameSite=Strict</c> and host-only, so the dashboard must be served from the host's own
    /// origin: host authentication cannot be combined with <see cref="SetCorsOrigins"/>.
    /// </remarks>
    /// <param name="configure">
    /// Sets the shell's view and operate policies, the sign-in challenge scheme, and the session's cookie name and
    /// lifetimes. A module's own policies override the shell's.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessDashboardSetupBuilder WithHostAuthentication(Action<DashboardHostAuthenticationOptions> configure)
    {
        Argument.IsNotNull(configure);

        var host = new DashboardHostAuthenticationOptions();
        configure(host);

        Auth.Mode = AuthMode.Host;
        Auth.Host = host;
        AuthConfigured = true;

        return this;
    }

    /// <summary>
    /// Signs operators in with one user name and password. The sign-in page takes the credential once, and the browser
    /// then holds only the session cookie.
    /// </summary>
    /// <remarks>
    /// The session cookie is protected by the host's Data Protection key ring, so every instance serving the dashboard
    /// must share that key ring. Changing the password ends every session issued under the old one at its next request.
    /// A script may still send the credential as a <c>Basic</c> authorization header on every request.
    /// </remarks>
    /// <param name="username">The user name.</param>
    /// <param name="password">The password.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="username"/> or <paramref name="password"/> is <see langword="null"/>, empty, or white space.
    /// </exception>
    public HeadlessDashboardSetupBuilder WithBasicAuth(string username, string password)
    {
        Argument.IsNotNullOrWhiteSpace(username);
        Argument.IsNotNullOrWhiteSpace(password);

        Auth.Mode = AuthMode.Basic;
        Auth.BasicCredentials = $"{username}:{password}".ToBase64();
        AuthConfigured = true;

        return this;
    }

    /// <summary>
    /// Signs operators and scripts in with one API key. The sign-in page takes the key once, and the browser then holds
    /// only the session cookie; a script sends the key as a bearer token.
    /// </summary>
    /// <remarks>
    /// The session cookie is protected by the host's Data Protection key ring, so every instance serving the dashboard
    /// must share that key ring. Changing the key ends every session issued under the old one at its next request.
    /// </remarks>
    /// <param name="apiKey">The API key.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is <see langword="null"/>, empty, or white space.</exception>
    public HeadlessDashboardSetupBuilder WithApiKey(string apiKey)
    {
        Argument.IsNotNullOrWhiteSpace(apiKey);

        Auth.Mode = AuthMode.ApiKey;
        Auth.ApiKey = apiKey;
        AuthConfigured = true;

        return this;
    }

    /// <summary>
    /// Serves the dashboard without sign-in, to anyone who can reach it. Only suitable for development or a trusted
    /// network.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public HeadlessDashboardSetupBuilder WithNoAuth()
    {
        Auth.Mode = AuthMode.None;
        AuthConfigured = true;

        return this;
    }

    /// <summary>
    /// Sets the path under the host's <c>PathBase</c> at which the dashboard is served. Defaults to
    /// <see cref="HeadlessDashboardOptions.DefaultBasePath"/>; the root is refused when the host starts.
    /// </summary>
    /// <param name="basePath">The base path, such as <c>/ops</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="basePath"/> is <see langword="null"/>, empty, or white space.</exception>
    public HeadlessDashboardSetupBuilder SetBasePath(string basePath)
    {
        _basePath = Argument.IsNotNullOrWhiteSpace(basePath);

        return this;
    }

    /// <summary>
    /// Sets the link to the host's tracing tool, so an object that carries a W3C trace id offers <c>Open trace</c>.
    /// </summary>
    /// <param name="template">The link, with <c>{traceId}</c> where the trace id goes.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="template"/> is <see langword="null"/>, empty, or white space.</exception>
    public HeadlessDashboardSetupBuilder SetTraceLinkTemplate(string template)
    {
        _traceLinkTemplate = Argument.IsNotNullOrWhiteSpace(template);

        return this;
    }

    /// <summary>
    /// Allows the given origins to call the dashboard's API cross-origin with credentials. Not allowed with host
    /// authentication.
    /// </summary>
    /// <param name="origins">The exact origins, such as <c>https://admin.example.com</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="origins"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="origins"/> is empty or holds an empty origin.</exception>
    public HeadlessDashboardSetupBuilder SetCorsOrigins(params string[] origins)
    {
        Argument.IsNotNullOrEmpty(origins);
        Argument.HasNoNullOrWhiteSpaceElements(origins);

        _corsOrigins = [.. origins];

        return this;
    }

    /// <summary>Adds sources to the dashboard's Content Security Policy.</summary>
    /// <param name="configure">Adds the sources.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessDashboardSetupBuilder ConfigureContentSecurityPolicy(
        Action<DashboardContentSecurityPolicy> configure
    )
    {
        Argument.IsNotNull(configure);

        _contentSecurityPolicyActions.Add(configure);

        return this;
    }

    /// <summary>Disables or reorders module contributions by id.</summary>
    /// <param name="configure">Sets the overrides, such as <c>c =&gt; c.Disable("messaging.nav.gateway")</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessDashboardSetupBuilder Contributions(Action<ContributionOverrides> configure)
    {
        Argument.IsNotNull(configure);

        _contributionActions.Add(configure);

        return this;
    }

    /// <summary>
    /// Binds contribution overrides from configuration, usually the <c>Headless:Dashboard:Contributions</c> section,
    /// with the keys <c>Disable</c> and <c>Order</c>. Adds to any overrides set in code.
    /// </summary>
    /// <param name="configuration">The configuration section.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public HeadlessDashboardSetupBuilder Contributions(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        _contributionSections.Add(configuration);

        return this;
    }

    /// <summary>
    /// Adds a module to the dashboard. Module packages call this from their own <c>Add{Module}</c> extension; a host
    /// calls that extension instead.
    /// </summary>
    /// <remarks>
    /// The module is resolved from the container as a singleton. Its access policies are read from
    /// <typeparamref name="TOptions"/>, so register and configure those options on <see cref="Services"/> as the
    /// module package's other options are.
    /// </remarks>
    /// <typeparam name="TModule">The module.</typeparam>
    /// <typeparam name="TOptions">The module's options, carrying its view and operate policies.</typeparam>
    /// <param name="id">
    /// The module's id, equal to <see cref="IDashboardModule.Id"/>: a lower-case kebab token such as <c>jobs</c>, used
    /// as a URL segment, so <c>api</c>, <c>auth</c>, <c>m</c>, <c>needs-you</c>, <c>sign-in</c>, and <c>shell</c> are
    /// reserved.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is not a lower-case kebab token, or is reserved.
    /// </exception>
    /// <exception cref="InvalidOperationException">A module with the same id is already added.</exception>
    public HeadlessDashboardSetupBuilder AddModule<TModule, TOptions>(string id)
        where TModule : class, IDashboardModule
        where TOptions : DashboardModuleOptions
    {
        Argument.IsNotNullOrWhiteSpace(id);

        if (!_ModuleIdRegex.IsMatch(id))
        {
            throw new ArgumentException(
                $"Dashboard module id '{id}' is not a lower-case kebab token such as 'jobs' or 'audit-log'.",
                nameof(id)
            );
        }

        if (_ReservedModuleIds.Contains(id))
        {
            throw new ArgumentException(
                $"Dashboard module id '{id}' is reserved by the dashboard shell. Reserved ids: "
                    + string.Join(", ", _ReservedModuleIds.Order(StringComparer.Ordinal))
                    + ".",
                nameof(id)
            );
        }

        if (Modules.Exists(module => string.Equals(module.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"A dashboard module with the id '{id}' is already added.");
        }

        Services.TryAddSingleton<TModule>();

        Modules.Add(
            new DashboardModuleDescriptor
            {
                Id = id,
                ResolveModule = static services => services.GetRequiredService<TModule>(),
                ResolveOptions = static services => services.GetRequiredService<IOptions<TOptions>>().Value,
            }
        );

        return this;
    }

    /// <summary>Checks that the registration chose an authentication mode.</summary>
    /// <exception cref="InvalidOperationException">No authentication mode was chosen.</exception>
    internal void Validate()
    {
        if (!AuthConfigured)
        {
            throw new InvalidOperationException(
                "Headless dashboard authentication was not configured. Call WithHostAuthentication, WithBasicAuth, or "
                    + "WithApiKey to secure the dashboard, or WithNoAuth to run it unauthenticated (development or "
                    + "trusted-network use only)."
            );
        }
    }

    internal void ApplyTo(HeadlessDashboardOptions options)
    {
        options.AuthMode = Auth.Mode;
        options.BasePath = _basePath;
        options.TraceLinkTemplate = _traceLinkTemplate;

        foreach (var origin in _corsOrigins)
        {
            options.CorsOrigins.Add(origin);
        }

        foreach (var configure in _contentSecurityPolicyActions)
        {
            configure(options.ContentSecurityPolicy);
        }

        foreach (var section in _contributionSections)
        {
            section.Bind(options.Contributions);
        }

        foreach (var configure in _contributionActions)
        {
            configure(options.Contributions);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex _ModuleIdRegex { get; }
}
