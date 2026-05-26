// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

            if (setup.Extensions.Count != 1)
            {
                throw new InvalidOperationException(
                    setup.Extensions.Count == 0
                        ? "Headless.AuditLog requires exactly one storage provider. Call one of `UseEntityFramework`, `UsePostgreSql`, or `UseSqlServer`."
                        : "Headless.AuditLog requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            // Cross-call guard — calling AddHeadlessAuditLog(setup => …) twice on the same
            // IServiceCollection would otherwise wire two storage providers (and duplicate
            // initializers/options/services) without diagnostic. Mirrors the sentinel pattern
            // already in Settings/Permissions/Features Setup classes.
            if (services.Any(static d => d.ServiceType == typeof(AuditLogStorageProviderRegistration)))
            {
                throw new InvalidOperationException(
                    "Headless.AuditLog requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            services.AddSingleton(
                new AuditLogStorageProviderRegistration(setup.Extensions.Single().GetType().FullName ?? "unknown")
            );

            services.Configure<AuditLogStorageOptions, AuditLogStorageOptionsValidator>(options =>
                setup.StorageOptions.CopyTo(options)
            );

            foreach (var extension in setup.Extensions)
            {
                extension.AddServices(services);
            }

            return new HeadlessAuditLogBuilder(services);
        }
    }

    private sealed record AuditLogStorageProviderRegistration(string Provider);
}
