// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            static (builder, name, tags) =>
                builder.AddDbContextCheck<TDbContext>(
                    name,
                    failureStatus: null,
                    tags,
                    customTestQuery: (context, cancellationToken) => _TestAsync(context, name, cancellationToken)
                ),
            HeadlessHealthCheckTags.Database
        );
    }

    /// <summary>
    /// Runs the check's configured <see cref="HeadlessHealthCheckOptions.TestCommand" />, or Microsoft's default
    /// <c>Database.CanConnectAsync</c> when none is set.
    /// </summary>
    /// <remarks>
    /// Microsoft's test query receives only the context, so the options come from the application service provider the
    /// context was built with; a context without one keeps the default probe.
    /// </remarks>
    private static async Task<bool> _TestAsync<TDbContext>(
        TDbContext context,
        string name,
        CancellationToken cancellationToken
    )
        where TDbContext : HeadlessDbContext
    {
        var testCommand = context
            .GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider?.GetService<IOptionsMonitor<HeadlessHealthCheckOptions>>()
            ?.Get(name)
            .TestCommand;

        if (testCommand is null)
        {
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable EF1002 // The command is the application's own configured probe, never request input.
        await context.Database.ExecuteSqlRawAsync(testCommand, cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002

        return true;
    }
}
