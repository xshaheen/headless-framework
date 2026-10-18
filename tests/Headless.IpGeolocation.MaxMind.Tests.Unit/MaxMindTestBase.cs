// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IpGeolocation;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>Builds a MaxMind provider over a private database directory and a stub download server.</summary>
public abstract class MaxMindTestBase : TestBase
{
    private readonly List<ServiceProvider> _providers = [];

    protected string DatabaseDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "headless-ipgeo-" + Guid.NewGuid().ToString("N"));

    internal StubMaxMindHandler Handler { get; } = new();

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero));

    protected ServiceProvider BuildProvider(Action<MaxMindOptions>? configure = null, bool withCredentials = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddHeadlessIpGeolocation(geo =>
            geo.UseMaxMind(options =>
            {
                options.DatabaseDirectory = DatabaseDirectory;

                if (withCredentials)
                {
                    options.AccountId = "42";
                    options.LicenseKey = "test-license-key";
                }

                configure?.Invoke(options);
            })
        );
        services.AddHttpClient(MaxMindOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider;
    }

    /// <summary>Polls <paramref name="condition" /> until it holds, failing the test after 10 seconds.</summary>
    protected static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        Handler.Dispose();

        if (Directory.Exists(DatabaseDirectory))
        {
            Directory.Delete(DatabaseDirectory, recursive: true);
        }

        return base.DisposeAsyncCore();
    }
}
