// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Headless.Settings.Internal;

/// <summary>
/// Startup gate that verifies the registered <typeparamref name="TContext"/> has been configured
/// with the Headless settings entities (<see cref="SettingValueRecord"/> and
/// <see cref="SettingDefinitionRecord"/>). Throws at startup when the required EF model
/// configuration is missing.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type to inspect at startup.</typeparam>
/// <param name="dbFactory">Factory used to obtain a <typeparamref name="TContext"/> for model inspection.</param>
internal sealed class SettingsEntityValidationStartupGate<TContext>(IDbContextFactory<TContext> dbFactory)
    : IHostedLifecycleService
    where TContext : DbContext
{
    /// <summary>
    /// Verifies that both <see cref="SettingValueRecord"/> and <see cref="SettingDefinitionRecord"/>
    /// are registered in the EF model before the host starts accepting requests.
    /// </summary>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TContext"/> does not contain the required settings entity type.
    /// Ensure <c>modelBuilder.AddHeadlessSettings(…)</c> is called in <c>OnModelCreating</c>.
    /// </exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(SettingValueRecord), nameof(SettingValueRecord));
        _EnsureEntity(context, typeof(SettingDefinitionRecord), nameof(SettingDefinitionRecord));
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
    /// Asserts that <paramref name="entityType"/> is present in <paramref name="context"/>'s EF model.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> whose model is inspected.</param>
    /// <param name="entityType">The CLR type to locate in the model.</param>
    /// <param name="entityName">Human-readable name used in the exception message.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="entityType"/> is not found in <paramref name="context"/>'s model.
    /// </exception>
    private static void _EnsureEntity(DbContext context, Type entityType, string entityName)
    {
        if (context.Model.FindEntityType(entityType) is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Headless.Settings: the registered DbContext `{context.GetType().FullName}` does not contain `{entityName}`. "
                + "Call `modelBuilder.AddHeadlessSettings(settingsStorageOptions)` in your `OnModelCreating`."
        );
    }
}
