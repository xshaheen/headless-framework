// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>Options-builder helpers for wiring a plain <c>AddDbContext</c> call alongside Headless services.</summary>
[PublicAPI]
public static class SetupOptionsExtension
{
    /// <summary>
    /// Applies <see cref="IInterceptor" /> services registered in the application container to the context
    /// options. EF Core does <b>not</b> auto-discover interceptors from the application service provider —
    /// they must be added to the options explicitly, so without this seam package-registered interceptors
    /// (e.g. the commit-coordination transaction interceptor) would silently never fire.
    /// </summary>
    /// <remarks>
    /// Instances the consumer already added through its own options action are skipped (reference equality)
    /// so an interceptor never runs twice per edge. Interceptors resolve from <paramref name="serviceProvider" />, so
    /// a scoped <see cref="IInterceptor" /> needs scoped options; with singleton or pooled options it resolves from the
    /// root and fails scope validation. The framework's own interceptors are singletons.
    /// <para>
    /// <c>AddHeadlessDbContext</c> / <c>AddHeadlessIdentityDbContext</c> call this automatically. Consumers wiring a
    /// plain <see cref="DbContext" /> via <c>AddDbContext</c> can call it from their options action
    /// (<c>(sp, options) =&gt; options.UseX(...).AddDiRegisteredInterceptors(sp)</c>) to pick up package-registered
    /// interceptors — e.g. the commit-coordination transaction interceptor — without hand-rolling the discovery.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="optionsBuilder"/> or <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    public static DbContextOptionsBuilder AddDiRegisteredInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider
    )
    {
        Argument.IsNotNull(optionsBuilder);
        Argument.IsNotNull(serviceProvider);

        var existing = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;

        var missing = serviceProvider
            .GetServices<IInterceptor>()
            .Where(interceptor => existing?.Any(e => ReferenceEquals(e, interceptor)) != true)
            .ToArray();

        if (missing.Length > 0)
        {
            optionsBuilder.AddInterceptors(missing);
        }

        return optionsBuilder;
    }
}
