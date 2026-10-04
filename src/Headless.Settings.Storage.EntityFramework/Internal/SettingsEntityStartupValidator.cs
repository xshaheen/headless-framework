// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings.Internal;

/// <summary>
/// Startup validator that verifies the registered <typeparamref name="TContext"/> has been configured
/// with the Headless settings entities (<see cref="SettingValueRecord"/> and
/// <see cref="SettingDefinitionRecord"/>). Throws at startup when the required EF model
/// configuration is missing.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext"/> type to inspect at startup.</typeparam>
/// <param name="services">
/// Provider the factory is resolved from inside <c>ValidateAsync</c>, not the constructor: the startup runner builds every
/// validator up front, so a missing factory would otherwise fail its activation before the required-service check reports it.
/// </param>
internal sealed class SettingsEntityStartupValidator<TContext>(IServiceProvider services) : IHeadlessStartupValidator
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
    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();

        if (dbFactory is null)
        {
            return;
        }

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        _EnsureEntity(context, typeof(SettingValueRecord), nameof(SettingValueRecord));
        _EnsureEntity(context, typeof(SettingDefinitionRecord), nameof(SettingDefinitionRecord));
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
                + "Call `modelBuilder.AddHeadlessSettings(this)` in your `OnModelCreating`."
        );
    }
}
