// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Settings;
using Headless.Settings.Internal;
using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupSettings
{
    extension(HeadlessSettingsSetupBuilder setup)
    {
        public HeadlessSettingsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkSettingsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkSettingsOptionsExtension(Type dbContextType) : ISettingsStorageOptionsExtension
    {
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
    private sealed class EntityFrameworkSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
    {
        public EntityFrameworkSettingsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.SettingValuesTableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.SettingDefinitionsTableName).IsValidCrossProviderIdentifier();
        }
    }
}
