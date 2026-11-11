// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.AuditLog.Internal;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.AuditLog;

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
        /// <c>modelBuilder.ConfigureHeadlessAuditLog(this)</c> inside <c>OnModelCreating</c>, which is validated at
        /// application startup.
        /// </typeparam>
        /// <remarks>
        /// <para>
        /// This overload uses EF Core migrations for schema management, so it registers no provisioned resource. A startup gate validates that the registered
        /// <typeparamref name="TContext"/> fully configured <c>AuditLogEntry</c> through
        /// <c>modelBuilder.ConfigureHeadlessAuditLog(...)</c> and throws <see cref="InvalidOperationException"/>
        /// when the model did not run that configuration.
        /// </para>
        /// <para>
        /// This registration knows nothing about the database behind the context, and the startup gate refuses it
        /// on PostgreSQL and SQL Server. There, use <c>UsePostgreSqlEntityFramework&lt;TContext&gt;()</c> or
        /// <c>UseSqlServerEntityFramework&lt;TContext&gt;()</c> from that database's Entity Framework audit package:
        /// they add the migration helpers for an append-only or partitioned table, a retention purge that passes
        /// the append-only trigger, and, on PostgreSQL, <see cref="IAuditLogPartitions{TContext}"/>.
        /// </para>
        /// </remarks>
        public HeadlessAuditLogSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            setup.RegisterExtension(new EntityFrameworkAuditLogOptionsExtension(typeof(TContext)));

            return setup;
        }
    }

    /// <summary>
    /// The storage registration, shared with the database-specific Entity Framework audit packages.
    /// </summary>
    /// <param name="dbContextType">The context that owns the audit log table.</param>
    /// <param name="database">The database the registration is for; <see langword="null"/> for any.</param>
    /// <param name="addDatabaseServices">
    /// Registers what one database does differently, such as its retention purge. It runs first, so its services win
    /// over the defaults registered here.
    /// </param>
    internal sealed class EntityFrameworkAuditLogOptionsExtension(
        Type dbContextType,
        StorageProvider? database = null,
        Action<IServiceCollection, Type>? addDatabaseServices = null
    ) : IAuditLogStorageOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            addDatabaseServices?.Invoke(services, dbContextType);
            services.TryAddSingleton(new EfAuditLogDatabase(database));
            services.AddOptions<AuditLogStorageOptions, EntityFrameworkAuditLogStorageOptionsValidator>();
            services.TryAddScoped<IAuditLogStore, EfAuditLogStore>();
            services.TryAddScoped(
                typeof(IAuditLog<>).MakeGenericType(dbContextType),
                typeof(EfAuditLog<>).MakeGenericType(dbContextType)
            );
            services.TryAddScoped(
                typeof(IAuditLogWriter<>).MakeGenericType(dbContextType),
                typeof(EfAuditLogWriter<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IReadAuditLog<>).MakeGenericType(dbContextType),
                typeof(EfReadAuditLog<>).MakeGenericType(dbContextType)
            );
            services.TryAddSingleton(
                typeof(IAuditLogRetention<>).MakeGenericType(dbContextType),
                typeof(EfAuditLogRetention<>).MakeGenericType(dbContextType)
            );
            services.RequireSingletonService(
                typeof(IDbContextFactory<>).MakeGenericType(dbContextType),
                requiredBy: "Headless audit log EF storage",
                remedy: "Register it at the default singleton lifetime: AddDbContextFactory<TContext>() or AddPooledDbContextFactory<TContext>() for a plain DbContext, or AddHeadlessDbContext<TContext>() or AddHeadlessDbContextPool<TContext>() for a HeadlessDbContext. The store is a singleton and would keep one scoped or transient factory for the life of the host."
            );
            services.AddStartupValidator(typeof(AuditLogEntityStartupValidator<>).MakeGenericType(dbContextType));
        }
    }

    // EF dispatches to whatever DB the consumer wired up, so the validator uses the most
    // permissive identifier pattern (SqlServer, a superset of PostgreSQL's character set) and
    // the larger length cap (SqlServer), and accepts any JsonColumnType. The underlying DB
    // surfaces type/length issues at migration time, except derived key and index names, which
    // PostgreSQL truncates instead of rejecting, so those are bounded here.
    private sealed class EntityFrameworkAuditLogStorageOptionsValidator : AbstractValidator<AuditLogStorageOptions>
    {
        public EntityFrameworkAuditLogStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidCrossProviderIdentifier();
            RuleFor(x => x.TableName)
                .IsValidCrossProviderIdentifier()
                .FitsDerivedPostgreSqlNames(AuditLogStorageNames.Indexes)
                .When(x => x.TableName is not null);
            RuleFor(x => x.JsonColumnType).IsInEnum().When(x => x.JsonColumnType.HasValue);
            RuleFor(x => x.CreatedAtColumnType)
                .MaximumLength(64)
                .Matches(@"^[A-Za-z][A-Za-z0-9 ]*(\([0-9]+\))?$")
                .When(x => !string.IsNullOrEmpty(x.CreatedAtColumnType));
            RuleFor(x => x.PurgeAuthority).IsInEnum();
            RuleFor(x => x.PartitionMonthsAhead).InclusiveBetween(0, AuditLogStorageOptions.MaxPartitionMonthsAhead);
            // The application's migrations own this table, so the provider has no schema step to create these in. An
            // option that the provider accepted and then ignored would read as a guarantee it does not give.
            RuleFor(x => x.AppendOnly)
                .Must(appendOnly => !appendOnly)
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.AppendOnly)} applies to the PostgreSql and SqlServer audit-log providers. With Entity Framework storage, register UsePostgreSqlEntityFramework<TContext>() or UseSqlServerEntityFramework<TContext>() and call migrationBuilder.AddHeadlessAuditLogAppendOnly(...) in a migration; IAuditLogRetention<TContext> still purges through it."
                );
            RuleFor(x => x.Partitioning)
                .Equal(AuditLogPartitioning.None)
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.Partitioning)} applies to the PostgreSql audit-log provider. With Entity Framework storage, register UsePostgreSqlEntityFramework<TContext>() and call migrationBuilder.AddHeadlessAuditLogPartitions(...) in the migration that creates the table; IAuditLogPartitions<TContext> then keeps the partitions."
                );
            RuleFor(x => x.MinimumRetention)
                .Null()
                .WithMessage(
                    $"{nameof(AuditLogStorageOptions.MinimumRetention)} applies to the PostgreSql audit-log provider. With Entity Framework storage, pass minimumRetention to the migration helpers."
                );
        }
    }
}
