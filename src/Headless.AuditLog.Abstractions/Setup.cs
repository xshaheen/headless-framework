// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.AuditLog;

/// <summary>Extension methods for registering the audit log abstractions.</summary>
[PublicAPI]
public static class AuditLogSetup
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
    }
}
