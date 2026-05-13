// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.AuditLog;

/// <summary>Extension methods for registering the EF Core audit log implementation.</summary>
[PublicAPI]
public static class SetupAuditLogEntityFramework
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers EF Core implementations of <see cref="IAuditChangeCapture"/>,
        /// <see cref="IAuditLogStore"/>, and per-context <see cref="IAuditLog{TContext}"/> /
        /// <see cref="IReadAuditLog{TContext}"/>. Generic on <typeparamref name="TContext"/> so
        /// multi-context applications wire a distinct binding per owning context instead of
        /// silently aliasing to whichever context registered first.
        /// Requires <c>AddHeadlessAuditLog()</c> to be called first for options registration,
        /// and <c>AddHeadlessDbContext&lt;T&gt;()</c> for the <c>DbContext</c> registration.
        /// </summary>
        /// <typeparam name="TContext">The EF Core context that owns the audit log table.</typeparam>
        public IServiceCollection AddHeadlessAuditLogEntity<TContext>()
            where TContext : DbContext
        {
            services.TryAddScoped<IAuditChangeCapture, EfAuditChangeCapture>();
            services.TryAddScoped<IAuditLogStore, EfAuditLogStore>();
            services.TryAddScoped<IAuditLog<TContext>, EfAuditLog<TContext>>();
            services.TryAddScoped<IReadAuditLog<TContext>, EfReadAuditLog<TContext>>();
            return services;
        }
    }
}
