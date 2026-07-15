// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Headless.Features.Internal;

/// <summary>
/// Hosted lifecycle service that validates, at startup, that the registered <typeparamref name="TContext"/>
/// has mapped all required Headless feature entities. Fails fast if <c>modelBuilder.AddHeadlessFeatures</c>
/// was not called in <c>OnModelCreating</c>.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type to validate.</typeparam>
/// <param name="dbFactory">Factory used to obtain a <typeparamref name="TContext"/> for model inspection.</param>
internal sealed class FeaturesEntityValidationStartupGate<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHostedLifecycleService
    where TContext : DbContext
{
    /// <summary>Validates that all required feature entity types are registered in the EF model.</summary>
    /// <param name="cancellationToken">Token to cancel context creation.</param>
    /// <exception cref="InvalidOperationException">
    /// The <typeparamref name="TContext"/> does not contain one of the required feature entity types
    /// (<see cref="FeatureValueRecord"/>, <see cref="FeatureDefinitionRecord"/>, or
    /// <see cref="FeatureGroupDefinitionRecord"/>). Ensure <c>modelBuilder.AddHeadlessFeatures(...)</c>
    /// is called in <c>OnModelCreating</c>.
    /// </exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(FeatureValueRecord), nameof(FeatureValueRecord));
        _EnsureEntity(context, typeof(FeatureDefinitionRecord), nameof(FeatureDefinitionRecord));
        _EnsureEntity(context, typeof(FeatureGroupDefinitionRecord), nameof(FeatureGroupDefinitionRecord));
    }

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
                + "Call `modelBuilder.AddHeadlessFeatures(featuresStorageOptions)` in your `OnModelCreating`."
        );
    }
}
