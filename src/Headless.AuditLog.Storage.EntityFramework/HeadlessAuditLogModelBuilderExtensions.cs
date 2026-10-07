// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace Microsoft.EntityFrameworkCore;

/// <summary>ModelBuilder extensions for configuring the audit log schema.</summary>
[PublicAPI]
public static class HeadlessAuditLogModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Registers and configures the <see cref="AuditLogEntry"/> entity type, resolving
        /// <see cref="AuditLogStorageOptions"/> from the <paramref name="context"/>'s service provider and the naming
        /// style from its database provider (snake_case on PostgreSQL, PascalCase elsewhere). Call from
        /// <c>OnModelCreating</c> with <c>modelBuilder.ConfigureHeadlessAuditLog(this)</c> to avoid injecting the options
        /// into the context.
        /// </summary>
        /// <param name="context">
        /// The <see cref="DbContext"/> whose service provider is used to resolve <see cref="AuditLogStorageOptions"/>.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <remarks>
        /// This method is idempotent. If the audit log entity is already configured, subsequent calls are no-ops.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        public ModelBuilder ConfigureHeadlessAuditLog(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            var options = context.GetService<IOptions<AuditLogStorageOptions>>().Value;
            var style = HeadlessStorageNaming.ForProvider(context.Database.ProviderName);

            return modelBuilder.ConfigureHeadlessAuditLog(options, style);
        }

        /// <summary>
        /// Registers and configures the <see cref="AuditLogEntry"/> entity type with the supplied
        /// <paramref name="options"/>. Call this from your <c>DbContext.OnModelCreating</c>.
        /// </summary>
        /// <param name="options">Audit log storage options.</param>
        /// <param name="style">
        /// The naming style of the database the model targets. It must match the database: the raw providers and
        /// <c>ConfigureHeadlessAuditLog(DbContext)</c> use <see cref="StorageNamingStyle.SnakeCase"/> on PostgreSQL and
        /// <see cref="StorageNamingStyle.PascalCase"/> elsewhere; pass
        /// <c>HeadlessStorageNaming.ForProvider(Database.ProviderName)</c> to derive it.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <remarks>
        /// This method is idempotent. If the audit log entity is already configured, subsequent calls are no-ops.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public ModelBuilder ConfigureHeadlessAuditLog(AuditLogStorageOptions options, StorageNamingStyle style)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(options);

            if (modelBuilder.Model.FindAnnotation(AuditLogStorageModelAnnotations.IsConfigured)?.Value is true)
            {
                return modelBuilder;
            }

            modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration(options, style));
            modelBuilder.Model.SetAnnotation(AuditLogStorageModelAnnotations.IsConfigured, value: true);
            return modelBuilder;
        }
    }
}
