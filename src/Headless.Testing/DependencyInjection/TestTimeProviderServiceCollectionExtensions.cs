// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Headless.Testing.DependencyInjection;

/// <summary>
/// Extension methods for registering test time provider services.
/// </summary>
[PublicAPI]
public static class TestTimeProviderServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the <see cref="TimeProvider"/> registration with a <see cref="FakeTimeProvider"/> so tests can
    /// freeze and advance time deterministically.
    /// </summary>
    /// <remarks>
    /// Uses <c>RemoveAll</c> + <c>AddSingleton</c> so it overrides any prior registration, including the
    /// <c>TryAddSingleton(TimeProvider.System)</c> that provider packages register defensively.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The <see cref="FakeTimeProvider"/> instance registered in the container,
    /// allowing callers to advance or set time in tests.</returns>
    public static FakeTimeProvider AddTestTimeProvider(this IServiceCollection services)
    {
        var fakeTimeProvider = new FakeTimeProvider();

        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(fakeTimeProvider);

        return fakeTimeProvider;
    }
}
