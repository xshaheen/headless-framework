// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>Contributes the readiness health check of a registered <see cref="HeadlessDbContext" />.</summary>
/// <remarks>
/// The check is Microsoft's first-party <c>AddDbContextCheck&lt;TContext&gt;</c>, which runs
/// <c>Database.CanConnectAsync</c> (or a <c>DbContextHealthCheckOptions&lt;TContext&gt;.CustomTestQuery</c> the app
/// configures under the check's name) on a context from the check's own scope. The Headless registration wraps it so a
/// failure keeps the registration's failure status and fixed text instead of the driver's exception message.
/// </remarks>
internal static class HeadlessDbContextHealthCheck
{
    /// <summary>The registration name of the check for <typeparamref name="TDbContext" />.</summary>
    public static string NameOf<TDbContext>()
        where TDbContext : HeadlessDbContext
    {
        return "dbcontext-" + (typeof(TDbContext).FullName ?? typeof(TDbContext).Name);
    }

    public static void Add<TDbContext>(IServiceCollection services)
        where TDbContext : HeadlessDbContext
    {
        services.AddHeadlessHealthCheck(
            NameOf<TDbContext>(),
            static (builder, name, tags) => builder.AddDbContextCheck<TDbContext>(name, failureStatus: null, tags),
            HeadlessHealthCheckTags.Database
        );
    }
}
