// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Checks;
using Headless.Hosting.Initialization;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for registering the audit log.</summary>
[PublicAPI]
public static class SetupAuditLog
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers audit log options. Add exactly one storage provider through
        /// <c>AddHeadlessAuditLog(setup =&gt; setup.Use...)</c>.
        /// </summary>
        public IServiceCollection AddHeadlessAuditLog(Action<AuditLogOptions>? configure = null)
        {
            services.AddOptions<AuditLogOptions, AuditLogOptionsValidator>();

            if (configure is not null)
            {
                services.Configure(configure);
            }

            return services;
        }

        /// <summary>
        /// Registers the audit log + the configured storage provider. Exactly one
        /// <c>setup.Use…</c> call (EntityFramework, PostgreSql, or SqlServer) is required.
        /// </summary>
        public HeadlessAuditLogBuilder AddHeadlessAuditLog(Action<HeadlessAuditLogSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessAuditLogSetupBuilder(services);
            configure(setup);

            // Single AuditLogOptions registration funneled through the no-args overload so consumers
            // don't have to call AddHeadlessAuditLog twice. ConfigureOptions on the builder, if used,
            // is applied here as the AuditLogOptions configurator.
            services.AddHeadlessAuditLog(setup.OptionsConfigurator);

            services.GuardSingleStorageProvider(
                setup.Extensions.Count,
                setup.Extensions.Count == 1 ? setup.Extensions.Single().GetType().FullName ?? "unknown" : "unknown",
                "Headless.AuditLog",
                ["UseEntityFramework", "UsePostgreSql", "UseSqlServer"],
                static name => new AuditLogStorageProviderRegistration(name)
            );

            services.Configure<AuditLogStorageOptions>(options => setup.StorageOptions.CopyTo(options));

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return new HeadlessAuditLogBuilder(services);
        }
    }

    private sealed record AuditLogStorageProviderRegistration(string Provider);
}
