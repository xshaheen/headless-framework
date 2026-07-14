// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Settings.Internal;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Settings;

/// <summary>
/// Provides the <c>UseEntityFramework</c> extension member on <see cref="HeadlessSettingsSetupBuilder"/>
/// that wires EF Core as the settings storage backend.
/// </summary>
[PublicAPI]
public static class SetupSettingsEntityFramework
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        /// <summary>
        /// Configures EF Core as the settings storage backend, using <typeparamref name="TContext"/>
        /// as the database context.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <see cref="DbContext"/> type that has been configured with
        /// <c>modelBuilder.AddHeadlessSettings(…)</c> in its <c>OnModelCreating</c> override.
        /// </typeparam>
        /// <returns>The same <see cref="HeadlessSettingsSetupBuilder"/> to allow chaining.</returns>
        public HeadlessSettingsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkSettingsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    /// <summary>
    /// <see cref="ISettingsStorageOptionsExtension"/> that registers EF Core repository
    /// implementations and the startup validation gate for a given <see cref="DbContext"/> type.
    /// </summary>
    /// <param name="dbContextType">The concrete <see cref="DbContext"/> CLR type to use.</param>
    private sealed class EntityFrameworkSettingsOptionsExtension(Type dbContextType) : ISettingsStorageOptionsExtension
    {
        /// <inheritdoc/>
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<SettingsStorageOptions, EntityFrameworkSettingsStorageOptionsValidator>();
            services.TryAddSingleton(
                typeof(ISettingValueRecordRepository),
                typeof(EfSettingValueRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(ISettingDefinitionRecordRepository),
                typeof(EfSettingDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(SettingsEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer). The underlying DB surfaces type/length issues at
    // migration time.
    /// <summary>
    /// Validates <see cref="SettingsStorageOptions"/> using cross-provider identifier rules
    /// (SQL Server superset) so that the same configuration is accepted by any EF provider.
    /// </summary>
    private sealed class EntityFrameworkSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
    {
        /// <summary>Initialises validation rules for table names and schema.</summary>
        public EntityFrameworkSettingsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.SettingValuesTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.SettingDefinitionsTableName).IsValidCrossProviderIdentifier();
        }
    }
}
