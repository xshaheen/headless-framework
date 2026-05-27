// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupAuditLogEntityFramework
{
    extension(HeadlessAuditLogSetupBuilder setup)
    {
        public HeadlessAuditLogSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkAuditLogOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    private sealed class EntityFrameworkAuditLogOptionsExtension(Type dbContextType) : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<AuditLogStorageOptions, EntityFrameworkAuditLogStorageOptionsValidator>();
            services.TryAddScoped<IAuditLogStore, EfAuditLogStore>();
            services.TryAddScoped(typeof(IAuditLog<>).MakeGenericType(dbContextType), typeof(EfAuditLog<>).MakeGenericType(dbContextType));
            services.TryAddSingleton(
                typeof(IReadAuditLog<>).MakeGenericType(dbContextType),
                typeof(EfReadAuditLog<>).MakeGenericType(dbContextType)
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton(
                    typeof(IHostedService),
                    typeof(AuditLogEntityValidationStartupGate<>).MakeGenericType(dbContextType)
                )
            );
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator caps at the most
    // permissive limit (SqlServerMaxLength) and accepts any JsonColumnType. The underlying DB
    // surfaces type/length issues at migration time.
    private sealed class EntityFrameworkAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public EntityFrameworkAuditLogStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PostgreSql.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
            RuleFor(x => x.TableName).NotEmpty().Matches(StorageIdentifier.PostgreSql.IdentifierPattern).MaximumLength(StorageIdentifier.SqlServer.IdentifierMaxLength);
            RuleFor(x => x.JsonColumnType).IsInEnum().When(x => x.JsonColumnType.HasValue);
            RuleFor(x => x.CreatedAtColumnType!)
                .MaximumLength(64)
                .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
                .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
        }
    }
}
