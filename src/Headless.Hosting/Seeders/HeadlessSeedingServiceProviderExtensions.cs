// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Runs the registered seeders from a test or a tool, through the same runner as the <c>seed</c> verb.</summary>
[PublicAPI]
public static class HeadlessSeedingServiceProviderExtensions
{
    /// <summary>
    /// Runs every registered <see cref="ISeeder" /> in dependency order, each from one DI scope, and stops at the first
    /// failure, as the <c>seed</c> verb does.
    /// </summary>
    /// <param name="services">The host's services, such as <c>host.Services</c>.</param>
    /// <param name="cancellationToken">Abandons the run.</param>
    /// <returns>A task that completes when every seeder has run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A seeder failed (its error is the inner exception), two seeders share a name, or two depend on each other.
    /// </exception>
    public static async Task SeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(services);

        await using var scope = services.CreateAsyncScope();
        var outcomes = await SeedRunner
            .RunAsync(SeedRunner.Ordered(scope.ServiceProvider), timeout: null, cancellationToken)
            .ConfigureAwait(false);

        SeedRunner.ThrowIfFailed(outcomes);
    }
}
