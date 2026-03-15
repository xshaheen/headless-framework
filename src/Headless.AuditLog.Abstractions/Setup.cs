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
        /// Registers audit log options. Call <c>AddHeadlessAuditLogEntityFramework()</c>
        /// (from <c>Headless.AuditLog.EntityFramework</c>) to add storage.
        /// </summary>
        public IServiceCollection AddHeadlessAuditLog(Action<AuditLogOptions>? configure = null)
        {
            services.AddOptions<AuditLogOptions, AuditLogOptionsValidator>();

            if (configure is not null)
                services.Configure(configure);

            return services;
        }
    }
}
