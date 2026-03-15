// Copyright (c) Mahmoud Shaheen. All rights reserved.

using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.AuditLog;

/// <summary>Extension methods for registering the EF Core audit log implementation.</summary>
[PublicAPI]
public static class AuditLogEntityFrameworkSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers EF Core implementations of <see cref="IAuditChangeCapture"/>,
        /// <see cref="IAuditLogStore"/>, and <see cref="IAuditLog"/>.
        /// Requires <c>AddHeadlessAuditLog()</c> to be called first for options registration,
        /// and <c>AddHeadlessDbContext&lt;T&gt;()</c> for the <c>DbContext</c> registration.
        /// </summary>
        public IServiceCollection AddHeadlessAuditLogEntityFramework()
        {
            services.TryAddScoped<IAuditChangeCapture, EfAuditChangeCapture>();
            services.TryAddScoped<IAuditLogStore, EfAuditLogStore>();
            services.TryAddScoped<IAuditLog, EfAuditLog>();
            return services;
        }
    }
}
