// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Headless.Testing.DependencyInjection;

/// <summary>
/// Extension methods for registering test time provider services.
/// </summary>
public static class TestTimeProviderServiceCollectionExtensions
{
    /// <summary>
    /// Replaces <see cref="TimeProvider"/> and <see cref="IClock"/> registrations with
    /// <see cref="FakeTimeProvider"/> and <see cref="TestClock"/> for testing.
    /// </summary>
    /// <remarks>
    /// This method uses <c>RemoveAll</c> + <c>AddSingleton</c> to handle all production registration
    /// paths: full <c>AddTimeService()</c> (Api), EF-only (<c>IClock</c> without <c>TimeProvider</c>),
    /// and no prior registration.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The <see cref="FakeTimeProvider"/> instance registered in the container,
    /// allowing callers to advance or set time in tests.</returns>
    public static FakeTimeProvider AddTestTimeProvider(this IServiceCollection services)
    {
        var fakeTimeProvider = new FakeTimeProvider();
        var testClock = new TestClock(fakeTimeProvider);

        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(fakeTimeProvider);

        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(testClock);

        return fakeTimeProvider;
    }
}
