// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Checks;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class HeadlessDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Replaces EF Core's default string primary-key value generation (a hyphenated <c>Guid.ToString()</c>) with a
    /// compact, no-hyphen guid (<c>ToString("N")</c>) produced by the application's registered
    /// <see cref="IGuidGenerator"/>. SQL Server contexts use the <see cref="SequentialGuidType.SqlServer"/> comb;
    /// other providers use <see cref="SequentialGuidType.Version7"/>. The compact <c>"N"</c> format is always
    /// guaranteed.
    /// </summary>
    public static DbContextOptionsBuilder GenerateCompactGuidForStringPrimaryKeys(this DbContextOptionsBuilder builder)
    {
        builder.ReplaceService<IValueGeneratorSelector, CompactGuidValueGeneratorSelector>();

        return builder;
    }

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

    #region Compact Guid

    private sealed class CompactGuidValueGeneratorSelector(ValueGeneratorSelectorDependencies dependencies)
        : RelationalValueGeneratorSelector(dependencies)
    {
        public override bool TryCreate(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
        {
            Argument.IsNotNull(property);
            Argument.IsNotNull(typeBase);

            if (property.ValueGenerated is ValueGenerated.Never)
            {
                return base.TryCreate(property, typeBase, out valueGenerator);
            }

            var propertyType = property.ClrType.UnwrapNullableType().UnwrapEnumType();

            if (propertyType == typeof(string))
            {
                // Generate temporary value if GetDefaultValueSql is set
                valueGenerator = new StringCompactGuidValueGenerator(property.GetDefaultValueSql() is not null);

                return true;
            }

            return base.TryCreate(property, typeBase, out valueGenerator);
        }
    }

    private sealed class StringCompactGuidValueGenerator(bool value) : ValueGenerator<string>
    {
        public override bool GeneratesTemporaryValues { get; } = value;

        // String primary keys share the framework's single guid source (fakeable in tests, swappable for a different
        // strategy), with the framework default when the context has no application service provider.
        public override string Next(EntityEntry entry)
        {
            return HeadlessGuidGeneratorResolver.Resolve(entry.Context).Create().ToString("N");
        }
    }

    #endregion
}
