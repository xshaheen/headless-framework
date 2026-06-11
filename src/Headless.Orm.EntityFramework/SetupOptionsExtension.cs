// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupOptionsExtension
{
    public static DbContextOptionsBuilder AddHeadlessExtension(this DbContextOptionsBuilder optionsBuilder)
    {
        Argument.IsNotNull(optionsBuilder);

        var ext = new HeadlessDbContextOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(ext);

        return optionsBuilder;
    }

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
    /// </remarks>
    internal static DbContextOptionsBuilder AddDiRegisteredInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider
    )
    {
        var existing = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;

        var missing = serviceProvider
            .GetServices<IInterceptor>()
            .Where(interceptor => existing is null || !existing.Any(e => ReferenceEquals(e, interceptor)))
            .ToArray();

        if (missing.Length > 0)
        {
            optionsBuilder.AddInterceptors(missing);
        }

        return optionsBuilder;
    }
}

/// <summary>Registers Headless EF Core services through DbContext options.</summary>
[PublicAPI]
public sealed class HeadlessDbContextOptionsExtension : IDbContextOptionsExtension
{
    public void ApplyServices(IServiceCollection services) => services.AddHeadlessDbContextServices();

    public void Validate(IDbContextOptions options) { }

    public DbContextOptionsExtensionInfo Info => new HeadlessOptionsExtensionInfo(this);

    private sealed class HeadlessOptionsExtensionInfo(IDbContextOptionsExtension e) : DbContextOptionsExtensionInfo(e)
    {
        public override string LogFragment => "HeadlessOptionsExtension";

        public override bool IsDatabaseProvider => false;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other is HeadlessOptionsExtensionInfo;
        }
    }
}
