// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Registers <see cref="ITenantIdentifierSource"/>s consulted by pre-auth tenant catalog resolution.</summary>
/// <remarks>
/// <para>
/// Ordering contract: sources are consulted in first-registration order and the first
/// <see cref="TenantIdentifierSourceResultKind.Found"/> result wins. Registering the same source
/// <strong>type</strong> through <see cref="AddSource{TSource}"/> twice keeps the first registration's
/// position and adds nothing; instance and delegate registrations always append.
/// </para>
/// <para>
/// The built-in host, route, and header sources register through this builder's
/// <c>AddHostSource</c> / <c>AddRouteSource</c> / <c>AddHeaderSource</c> members, each of which follows
/// the same type-deduplication rule while its options contributions accumulate.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessTenantCatalogResolutionBuilder
{
    private readonly IServiceCollection _services;

    internal HeadlessTenantCatalogResolutionBuilder(IServiceCollection services)
    {
        _services = Argument.IsNotNull(services);
    }

    /// <summary>Registers an <see cref="ITenantIdentifierSource"/> implementation resolved from DI.</summary>
    /// <typeparam name="TSource">The identifier source implementation type.</typeparam>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <remarks>
    /// Uses <c>TryAddEnumerable</c> semantics: a second registration of the same
    /// <typeparamref name="TSource"/> type keeps the first registration's position and is otherwise
    /// ignored — options contributions from every call still apply in call order.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddSource<TSource>()
        where TSource : class, ITenantIdentifierSource
    {
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantIdentifierSource, TSource>());
        return this;
    }

    /// <summary>Registers an already-constructed <see cref="ITenantIdentifierSource"/> instance.</summary>
    /// <param name="source">The identifier source instance.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>Instance registrations always append — two instances of one type both run.</remarks>
    public HeadlessTenantCatalogResolutionBuilder AddSource(ITenantIdentifierSource source)
    {
        Argument.IsNotNull(source);
        _services.AddSingleton(source);
        return this;
    }

    /// <summary>Registers a delegate that reads a raw tenant identifier from the current request.</summary>
    /// <param name="resolver">
    /// Delegate invoked once per request, in registration order. Returning <see langword="null"/> or a
    /// whitespace-only string means "no identifier from this source" and resolution continues with the next
    /// registered source; the return value is otherwise raw caller input and is never trimmed, lowercased, or
    /// shape-validated here. Exceptions propagate unchanged, like a store fault — they are never mapped to a
    /// tenant outcome.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolver"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A convenience over implementing <see cref="ITenantIdentifierSource"/>: the delegate cannot express
    /// an ambiguous (<see cref="TenantIdentifierSourceResultKind.Invalid"/>) input — for that, implement
    /// the interface. Delegate registrations always append, in call order, after any earlier entries.
    /// </para>
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddSource(Func<HttpContext, string?> resolver)
    {
        Argument.IsNotNull(resolver);
        _services.AddSingleton<ITenantIdentifierSource>(new DelegateTenantIdentifierSource(resolver));
        return this;
    }

    /// <summary>Registers the built-in host tenant identifier source with one host template.</summary>
    /// <param name="template">
    /// The host template to append to <see cref="HostTenantIdentifierSourceOptions.Templates"/> — for
    /// example <c>{tenant}.example.com</c>, <c>{tenant}.*</c>, or a bare <c>{tenant}</c> for the
    /// whole-host (custom-domain) form.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="template"/> is <see langword="null"/> or whitespace.</exception>
    /// <remarks>
    /// Appends to the template list rather than replacing it: options contributions accumulate across
    /// calls while the source descriptor deduplicates, so
    /// <c>AddHostSource("{tenant}.a.com").AddHostSource("{tenant}.b.com")</c> yields one source
    /// matching both, first registration winning on overlap. Templates are validated at startup.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHostSource(string template)
    {
        Argument.IsNotNullOrWhiteSpace(template);

        _AddHostSourceCore(options => options.Templates.Add(template));

        return this;
    }

    /// <summary>Registers the built-in host tenant identifier source, configuring its options.</summary>
    /// <param name="configure">Callback to configure <see cref="HostTenantIdentifierSourceOptions"/>.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The callback runs as one additional <c>Configure</c> action per call; repeat registrations of
    /// the source type keep the first descriptor's position. Options are validated at
    /// startup — an empty template list or an unparsable template fails the host.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHostSource(Action<HostTenantIdentifierSourceOptions> configure)
    {
        Argument.IsNotNull(configure);

        _AddHostSourceCore(configure);

        return this;
    }

    /// <summary>
    /// Registers the built-in host tenant identifier source, binding its options from configuration.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section holding <see cref="HostTenantIdentifierSourceOptions"/> — for example
    /// <c>Tenant:Host</c> with a <c>Templates</c> array. The bind must yield at least one parseable
    /// template, or host startup fails.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Binds as an additional options contribution; repeat registrations of the source type keep the
    /// first descriptor's position.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHostSource(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        _AddSourceCore<
            HostTenantIdentifierSourceOptions,
            HostTenantIdentifierSourceOptionsValidator,
            HostTenantIdentifierSource
        >(options => options.Bind(configuration));

        return this;
    }

    private void _AddHostSourceCore(Action<HostTenantIdentifierSourceOptions> configure)
    {
        _AddSourceCore<
            HostTenantIdentifierSourceOptions,
            HostTenantIdentifierSourceOptionsValidator,
            HostTenantIdentifierSource
        >(options => options.Configure(configure));
    }

    /// <summary>Registers the built-in route tenant identifier source with the default route value name.</summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <remarks>
    /// Reads the <c>tenant</c> route value (see
    /// <see cref="RouteTenantIdentifierSourceOptions.DefaultRouteValueName"/>). Repeat registrations of
    /// the source type deduplicate while options contributions accumulate. Requires
    /// <see cref="SetupApiTenancy.UseHeadlessTenantCatalogResolution"/> to run after
    /// <c>UseRouting()</c>, or every request resolves as host context. Every overload also wraps
    /// the routing <see cref="LinkGenerator"/> once so generated links keep the tenant segment;
    /// see <see cref="RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue"/> to opt out.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddRouteSource()
    {
        _AddRouteSourceCore();

        return this;
    }

    /// <summary>Registers the built-in route tenant identifier source with a route value name.</summary>
    /// <param name="routeValueName">
    /// The route value name to read — for example <c>org</c> for an endpoint mapped at
    /// <c>/{org}/orders</c>. Must not be blank; validated at startup.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="routeValueName"/> is <see langword="null"/> or whitespace.</exception>
    /// <remarks>
    /// The name is one additional options contribution per call; repeat registrations of the source
    /// type keep the first descriptor's position and the last name contribution wins.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddRouteSource(string routeValueName)
    {
        Argument.IsNotNullOrWhiteSpace(routeValueName);

        _AddRouteSourceCore(options => options.Configure(o => o.RouteValueName = routeValueName));

        return this;
    }

    /// <summary>Registers the built-in route tenant identifier source, configuring its options.</summary>
    /// <param name="configure">Callback to configure <see cref="RouteTenantIdentifierSourceOptions"/>.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The callback runs as one additional <c>Configure</c> action per call; repeat registrations of
    /// the source type keep the first descriptor's position. A blank route value name fails
    /// host startup.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddRouteSource(Action<RouteTenantIdentifierSourceOptions> configure)
    {
        Argument.IsNotNull(configure);

        _AddRouteSourceCore(options => options.Configure(configure));

        return this;
    }

    /// <summary>
    /// Registers the built-in route tenant identifier source, binding its options from configuration.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section holding <see cref="RouteTenantIdentifierSourceOptions"/> — for
    /// example <c>Tenant:Route</c> with a <c>RouteValueName</c> key. The bound name must not be
    /// blank, or host startup fails.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Binds as an additional options contribution; repeat registrations of the source type keep the
    /// first descriptor's position.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddRouteSource(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        _AddRouteSourceCore(options => options.Bind(configuration));

        return this;
    }

    /// <summary>
    /// Shared core of the <c>AddRouteSource</c> overloads: registers the validated options type
    /// plus (optionally) one <c>Configure</c> contribution, and the deduplicating source descriptor.
    /// </summary>
    private void _AddRouteSourceCore(Action<OptionsBuilder<RouteTenantIdentifierSourceOptions>>? configure = null)
    {
        _AddSourceCore<
            RouteTenantIdentifierSourceOptions,
            RouteTenantIdentifierSourceOptionsValidator,
            RouteTenantIdentifierSource
        >(configure);

        _DecorateLinkGeneratorOnce();
    }

    /// <summary>
    /// Wraps the routing <see cref="LinkGenerator"/> with
    /// <see cref="TenantAmbientRouteValueLinkGenerator"/> exactly once across every
    /// <c>AddRouteSource</c> call.
    /// </summary>
    /// <remarks>
    /// The wrap cannot be made conditional on
    /// <see cref="RouteTenantIdentifierSourceOptions.PromoteAmbientRouteValue"/> here because options
    /// contributions (including <c>IConfiguration</c> binds) are only evaluated once the container is
    /// built; the decorator reads the switch per call instead. Consumers that replace the
    /// <see cref="LinkGenerator"/> registration after registering the route source remove the wrap.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No unkeyed <see cref="LinkGenerator"/> registration exists to decorate — impossible after
    /// <c>AddRouting()</c> unless a keyed-only or removed registration left the collection in that state.
    /// </exception>
    private void _DecorateLinkGeneratorOnce()
    {
        // AddRouting() is TryAdd-based: repeating it here only guarantees the LinkGenerator descriptor
        // exists to wrap, whether or not AddControllers()/AddRouting() ran before the route source, and
        // a later call of either cannot undo the wrap.
        _services.AddRouting();

        if (
            _services.Any(descriptor =>
                descriptor.ServiceType == typeof(TenantAmbientRouteValueLinkGenerator.RegistrationMarker)
            )
        )
        {
            return;
        }

        _services.AddSingleton(TenantAmbientRouteValueLinkGenerator.RegistrationMarker.Instance);

        if (!_services.TryDecorate<LinkGenerator, TenantAmbientRouteValueLinkGenerator>())
        {
            throw new InvalidOperationException(
                "AddRouteSource() could not decorate the routing LinkGenerator: no unkeyed LinkGenerator "
                    + "registration exists even though AddRouting() was called. Register the route source "
                    + "after any code that removes or re-keys the LinkGenerator service."
            );
        }
    }

    /// <summary>Registers the built-in header tenant identifier source with the default header name.</summary>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <remarks>
    /// Reads the <c>X-Tenant</c> header (see
    /// <see cref="HeaderTenantIdentifierSourceOptions.DefaultHeaderName"/>). Repeat registrations of
    /// the source type deduplicate while options contributions accumulate. A header can only
    /// select an existing enabled tenant through the catalog and mapping-integrity enforcement still applies to authenticated
    /// callers — but it bypasses any perimeter control bound to a tenant's hostname, so register a host
    /// source first where hostnames carry such controls.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHeaderSource()
    {
        _AddHeaderSourceCore();

        return this;
    }

    /// <summary>Registers the built-in header tenant identifier source with one header name.</summary>
    /// <param name="headerName">
    /// The header name to contribute to <see cref="HeaderTenantIdentifierSourceOptions.HeaderNames"/>.
    /// Must be an HTTP token (validated at startup).
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="headerName"/> is <see langword="null"/> or whitespace.</exception>
    /// <remarks>
    /// Replaces the list while it is still the untouched default, and appends once it has been
    /// customized: <c>AddHeaderSource("X-Legacy")</c> reads only <c>X-Legacy</c>, while
    /// <c>AddHeaderSource("X-Tenant").AddHeaderSource("X-Legacy-Tenant")</c> yields one source reading
    /// both under a single duplicate-detection scope. The default is never silently kept beside a
    /// requested name, so a client cannot select a tenant through a header the operator did not configure.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHeaderSource(string headerName)
    {
        Argument.IsNotNullOrWhiteSpace(headerName);

        _AddHeaderSourceCore(options => options.Configure(o => o.ContributeHeaderName(headerName)));

        return this;
    }

    /// <summary>Registers the built-in header tenant identifier source, configuring its options.</summary>
    /// <param name="configure">Callback to configure <see cref="HeaderTenantIdentifierSourceOptions"/>.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The callback runs as one additional <c>Configure</c> action per call; repeat registrations of
    /// the source type keep the first descriptor's position. Options are validated at startup —
    /// an empty list or a name that is not an HTTP token fails the host.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHeaderSource(Action<HeaderTenantIdentifierSourceOptions> configure)
    {
        Argument.IsNotNull(configure);

        _AddHeaderSourceCore(options => options.Configure(configure));

        return this;
    }

    /// <summary>
    /// Registers the built-in header tenant identifier source, binding its options from configuration.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section holding <see cref="HeaderTenantIdentifierSourceOptions"/> — for example
    /// <c>Tenant:Header</c> with a <c>HeaderNames</c> array. A section that lists names replaces the
    /// untouched default list (and appends to a customized one); a section without <c>HeaderNames</c>
    /// keeps the default.
    /// </param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Binds as an additional options contribution; repeat registrations of the source type keep the
    /// first descriptor's position.
    /// </remarks>
    public HeadlessTenantCatalogResolutionBuilder AddHeaderSource(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        _AddHeaderSourceCore(options =>
            options
                // The binder adds into the existing list, so a bound list would otherwise be appended to
                // the default rather than replace it — clear the untouched default first, only when the
                // section actually lists names, so an empty section keeps the default.
                .Configure(o =>
                {
                    if (
                        o.HasUntouchedDefaultHeaderNames
                        && configuration.GetSection(nameof(HeaderTenantIdentifierSourceOptions.HeaderNames)).Exists()
                    )
                    {
                        o.HeaderNames = [];
                    }
                })
                .Bind(configuration)
        );

        return this;
    }

    /// <summary>
    /// Shared core of the <c>AddHeaderSource</c> overloads: registers the validated options type
    /// plus (optionally) one <c>Configure</c> contribution, and the deduplicating source descriptor.
    /// </summary>
    private void _AddHeaderSourceCore(Action<OptionsBuilder<HeaderTenantIdentifierSourceOptions>>? configure = null)
    {
        _AddSourceCore<
            HeaderTenantIdentifierSourceOptions,
            HeaderTenantIdentifierSourceOptionsValidator,
            HeaderTenantIdentifierSource
        >(configure);
    }

    /// <summary>
    /// Shared registration core of every built-in source: the validated options type plus one optional
    /// options contribution (a <c>Configure</c> callback or a configuration bind, applied in call order so
    /// contributions accumulate), and the source descriptor with <c>TryAddEnumerable</c> semantics so a
    /// repeated registration of the same source type keeps its first position.
    /// </summary>
    private void _AddSourceCore<TOptions, TValidator, TSource>(Action<OptionsBuilder<TOptions>>? configure)
        where TOptions : class
        where TValidator : class, IValidator<TOptions>
        where TSource : class, ITenantIdentifierSource
    {
        var optionsBuilder = _services.AddOptions<TOptions, TValidator>();

        configure?.Invoke(optionsBuilder);

        _services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantIdentifierSource, TSource>());
    }
}
