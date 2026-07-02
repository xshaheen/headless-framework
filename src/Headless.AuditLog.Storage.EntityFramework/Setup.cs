// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
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
        /// <summary>
        /// Configures the audit log to persist entries through the specified EF Core
        /// <typeparamref name="TContext"/>. Audit entries are added to the same
        /// <c>DbContext</c> that is executing <c>SaveChanges</c> and commit atomically
        /// with the entity changes — no separate connection or transaction is opened.
        /// </summary>
        /// <typeparam name="TContext">
        /// The <c>DbContext</c> subclass that owns the audit log table. The context must call
        /// <see cref="AuditLogModelBuilderExtensions.AddHeadlessAuditLog"/> inside
        /// <c>OnModelCreating</c>, which is validated at application startup.
        /// </typeparam>
        /// <remarks>
        /// This overload uses EF Core migrations for schema management; the startup storage
        /// initializer (<see cref="AuditLogStorageOptions.InitializeOnStartup"/>) has no effect
        /// in EF mode. A startup gate validates that the registered
        /// <typeparamref name="TContext"/> contains the <c>AuditLogEntry</c> entity and throws
        /// <see cref="InvalidOperationException"/> when the model does not include it.
        /// </remarks>
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
            services.TryAddScoped(
                typeof(IAuditLog<>).MakeGenericType(dbContextType),
                typeof(EfAuditLog<>).MakeGenericType(dbContextType)
            );
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

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer), and accepts any JsonColumnType. The underlying DB
    // surfaces type/length issues at migration time.
    private sealed class EntityFrameworkAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public EntityFrameworkAuditLogStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.TableName).IsValidCrossProviderIdentifier();
            RuleFor(x => x.JsonColumnType).IsInEnum().When(x => x.JsonColumnType.HasValue);
            RuleFor(x => x.CreatedAtColumnType)
                .MaximumLength(64)
                .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
                .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
        }
    }
}
