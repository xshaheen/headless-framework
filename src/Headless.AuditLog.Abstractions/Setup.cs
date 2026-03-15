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
        /// Registers audit log options. Call <see cref="AddHeadlessAuditLog"/>
        /// (from <c>Headless.AuditLog.EntityFramework</c>) to add storage.
        /// </summary>
        public IServiceCollection AddHeadlessAuditLog(Action<AuditLogOptions>? configure = null)
        {
            var options = new AuditLogOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(Options.Create(options));
            return services;
        }
    }
}
