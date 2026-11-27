// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Internal;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features.Internal;

/// <summary>
/// Startup validator that checks, at startup, that the registered <typeparamref name="TContext"/>
/// has mapped all required Headless feature entities. Fails fast if <c>modelBuilder.ConfigureHeadlessFeatures</c>
/// was not called in <c>OnModelCreating</c>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type to validate.</typeparam>
/// <param name="services">
/// Provider the factory is resolved from inside <c>ValidateAsync</c>, not the constructor: the startup runner builds every
/// validator up front, so a missing factory would otherwise fail its activation before the required-service check reports it.
/// </param>
internal sealed class FeaturesEntityStartupValidator<TContext>(IServiceProvider services) : IHeadlessStartupValidator
    where TContext : DbContext
{
    /// <summary>Validates the required EF entity types and the SQL Server provider-key collation.</summary>
    /// <param name="cancellationToken">Token to cancel context creation.</param>
    /// <exception cref="InvalidOperationException">
    /// The <typeparamref name="TContext"/> does not contain one of the required feature entity types
    /// (<see cref="FeatureValueRecord"/>, <see cref="FeatureDefinitionRecord"/>, or
    /// <see cref="FeatureGroupDefinitionRecord"/>) or the required SQL Server collation.
    /// Ensure <c>modelBuilder.ConfigureHeadlessFeatures(...)</c>
    /// is called in <c>OnModelCreating</c>.
    /// </exception>
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();

        if (dbFactory is null)
        {
            return;
        }

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(FeatureValueRecord), nameof(FeatureValueRecord));
        _EnsureEntity(context, typeof(FeatureDefinitionRecord), nameof(FeatureDefinitionRecord));
        _EnsureEntity(context, typeof(FeatureGroupDefinitionRecord), nameof(FeatureGroupDefinitionRecord));
        SqlServerScopeCollation.EnsureConfigured(
            context,
            typeof(FeatureValueRecord),
            nameof(FeatureValueRecord.ProviderKey),
            nameof(HeadlessFeaturesModelBuilderExtensions.ConfigureHeadlessFeatures)
        );
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="entityType"/> is not present
    /// in the <paramref name="context"/>'s EF model.
    /// </summary>
    private static void _EnsureEntity(DbContext context, Type entityType, string entityName)
    {
        if (context.Model.FindEntityType(entityType) is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Headless.Features: the registered DbContext `{context.GetType().FullName}` does not contain `{entityName}`. "
                + "Call `modelBuilder.ConfigureHeadlessFeatures(this)` in your `OnModelCreating`."
        );
    }
}
