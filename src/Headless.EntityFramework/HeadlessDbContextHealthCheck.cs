// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>Contributes the readiness health check of a registered <see cref="HeadlessDbContext" />.</summary>
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
            static (provider, cancellationToken) => _ProbeAsync<TDbContext>(provider, cancellationToken),
            HeadlessHealthCheckTags.Database
        );
    }

    private static async Task _ProbeAsync<TDbContext>(IServiceProvider provider, CancellationToken cancellationToken)
        where TDbContext : HeadlessDbContext
    {
        // The health check runs in its own scope, so the context comes from that scope (or its pool) and is
        // released with it. CanConnectAsync reports a refused connection as false rather than throwing.
        var context = provider.GetRequiredService<TDbContext>();

        if (!await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"{typeof(TDbContext).Name} cannot connect to its database.");
        }
    }
}
