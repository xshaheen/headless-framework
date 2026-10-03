// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// Extension methods for attaching the <see cref="HeadlessDbContextOptionsExtension"/> to EF Core
/// options builders. Called automatically by <c>AddHeadlessDbContext</c>; only needed directly when
/// wiring a plain <c>AddDbContext</c> call alongside Headless services.
/// </summary>
[PublicAPI]
public static class SetupOptionsExtension
{
    /// <summary>
    /// Attaches the <see cref="HeadlessDbContextOptionsExtension"/> to the options builder, which in turn
    /// registers Headless EF Core infrastructure services when EF Core builds the internal service provider.
    /// </summary>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <returns>The same options builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder AddHeadlessExtension(this DbContextOptionsBuilder optionsBuilder)
    {
        Argument.IsNotNull(optionsBuilder);

        var ext = new HeadlessDbContextOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(ext);

        return optionsBuilder;
    }

    /// <summary>
    /// Attaches the <see cref="HeadlessDbContextOptionsExtension"/> to the strongly-typed options builder.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> type.</typeparam>
    /// <param name="optionsBuilder">The EF Core options builder to configure.</param>
    /// <returns>The same options builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder<TContext> AddHeadlessExtension<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder
    )
        where TContext : DbContext
    {
        Argument.IsNotNull(optionsBuilder);

        var ext = new HeadlessDbContextOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(ext);

        return optionsBuilder;
    }

    /// <summary>
    /// Applies <see cref="IInterceptor" /> services registered in the application container to the context
    /// options. EF Core does <b>not</b> auto-discover interceptors from the application service provider —
    /// they must be added to the options explicitly, so without this seam package-registered interceptors
    /// (e.g. the commit-coordination transaction interceptor) would silently never fire.
    /// </summary>
    /// <remarks>
    /// Instances the consumer already added through its own options action are skipped (reference equality)
    /// so an interceptor never runs twice per edge. Interceptors are expected to be registered as singletons
    /// (the framework's own are); a scoped <see cref="IInterceptor" /> combined with
    /// <c>optionsLifetime: ServiceLifetime.Singleton</c> is unsupported and fails scope validation.
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
