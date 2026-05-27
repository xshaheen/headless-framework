// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Permissions;
using Headless.Permissions.Internal;
using Headless.Permissions.Repositories;
using Headless.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPermissions
{
    extension(HeadlessPermissionsSetupBuilder setup)
    {
        public HeadlessPermissionsSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkPermissionsOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkPermissionsOptionsExtension(Type dbContextType) : IPermissionsStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<PermissionsStorageOptions, EntityFrameworkPermissionsStorageOptionsValidator>();
            services.TryAddSingleton(
                typeof(IPermissionGrantRepository),
                typeof(EfPermissionGrantRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IPermissionDefinitionRecordRepository),
                typeof(EfPermissionDefinitionRecordRepository<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(PermissionsEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator caps at the most
    // permissive limit (SqlServerMaxLength). PG-via-EF consumers with longer identifiers will
    // surface a clearer error from the EF migration than the validator could.
    private sealed class EntityFrameworkPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
    {
        public EntityFrameworkPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionGrantsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
            RuleFor(x => x.PermissionGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        }
    }
}
