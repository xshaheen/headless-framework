// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Extension methods on <see cref="ModelBuilder"/> for registering the Headless tenant catalog entity.</summary>
[PublicAPI]
public static class HeadlessMultiTenancyModelBuilderExtensions
{
    extension(ModelBuilder modelBuilder)
    {
        /// <summary>
        /// Applies the <see cref="TenantRecord"/> entity configuration, including the provider-specific
        /// unique-identifier collation (KTD6). Call from <c>OnModelCreating</c> with
        /// <c>modelBuilder.AddHeadlessTenancyCatalog(this)</c>.
        /// </summary>
        /// <param name="context">
        /// The <see cref="DbContext"/> being configured — used only to read
        /// <see cref="Infrastructure.DatabaseFacade.ProviderName"/> so the unique index can be pinned to a
        /// deterministic, case-sensitive collation on providers this package knows about (SQL Server,
        /// PostgreSQL). The provider must already be selected (e.g. via <c>UseNpgsql</c>/<c>UseSqlServer</c>
        /// on the options builder) by the time <c>OnModelCreating</c> runs, which is always the case.
        /// </param>
        /// <returns>The same <see cref="ModelBuilder"/> instance to allow chaining.</returns>
        /// <remarks>
        /// This method is idempotent. If the tenant catalog entity is already configured, subsequent
        /// calls are no-ops.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        public ModelBuilder AddHeadlessTenancyCatalog(DbContext context)
        {
            Argument.IsNotNull(modelBuilder);
            Argument.IsNotNull(context);

            if (modelBuilder.Model.FindAnnotation(TenantCatalogStorageModelAnnotations.IsConfigured)?.Value is true)
            {
                return modelBuilder;
            }

            modelBuilder.ApplyConfiguration(new TenantRecordConfiguration(context.Database.ProviderName));
            modelBuilder.Model.SetAnnotation(TenantCatalogStorageModelAnnotations.IsConfigured, value: true);

            return modelBuilder;
        }
    }
}
