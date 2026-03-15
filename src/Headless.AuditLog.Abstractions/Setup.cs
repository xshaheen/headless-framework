// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>Extension methods for registering the audit log abstractions.</summary>
[PublicAPI]
public static class AuditLogSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers audit log options. Call <c>AddHeadlessAuditLogEntityFramework()</c>
        /// (from <c>Headless.AuditLog.EntityFramework</c>) to add storage.
        /// </summary>
        public IServiceCollection AddHeadlessAuditLog(Action<AuditLogOptions>? configure = null)
        {
            services.AddOptions<AuditLogOptions>()
                .Validate(
                    static opts =>
                        opts.SensitiveDataStrategy != SensitiveDataStrategy.Transform
                        || opts.SensitiveValueTransformer is not null,
                    "SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform."
                )
                .ValidateOnStart();

            if (configure is not null)
                services.Configure(configure);

            return services;
        }
    }
}
