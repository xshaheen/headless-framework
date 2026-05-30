// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;
using Headless.Permissions;
using Headless.Permissions.Internal;
using Headless.Permissions.Repositories;
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

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer). The underlying DB surfaces type/length issues at
    // migration time.
    private sealed class EntityFrameworkPermissionsStorageOptionsValidator : AbstractValidator<PermissionsStorageOptions>
    {
        public EntityFrameworkPermissionsStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.SqlServer.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
            RuleFor(x => x.PermissionGrantsTableName).NotEmpty().Matches(StorageIdentifier.SqlServer.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
            RuleFor(x => x.PermissionDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.SqlServer.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
            RuleFor(x => x.PermissionGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.SqlServer.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
        }
    }
}
