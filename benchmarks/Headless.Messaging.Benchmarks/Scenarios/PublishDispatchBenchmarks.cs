// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Headless.Messaging.Benchmarks.Support;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Measures the per-message publish dispatch path (<see cref="PublishMiddlewarePipeline.ExecuteAsync{T}"/>).
/// Each call exercises publish-context creation, header snapshotting, middleware service resolution, and
/// delegate-chain construction without transport or storage I/O.
/// </summary>
[MemoryDiagnoser]
public class PublishDispatchBenchmarks
{
    private ServiceProvider _provider = null!;
    private PublishMiddlewarePipeline _pipeline = null!;
    private BenchmarkPayload _payload = null!;
    private PublishOptions _options = null!;

    [Params(0, 1, 5)]
    public int MiddlewareCount { get; set; }

    [Params(0, 8)]
    public int HeaderCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        for (var i = 0; i < MiddlewareCount; i++)
        {
            services.AddScoped<IPublishMiddleware<PublishContext>, NoOpPublishMiddleware>();
        }

        _provider = services.BuildServiceProvider();
        _pipeline = new PublishMiddlewarePipeline(_provider);
        _payload = new BenchmarkPayload("payload");
        _options = _CreateOptions(HeaderCount);

        _pipeline
            .ExecuteAsync(
                _payload,
                IntentType.Bus,
                _options,
                delayTime: null,
                static (_, _, _) => Task.CompletedTask,
                cancellationToken: CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public Task ExecuteDispatch()
    {
        return _pipeline.ExecuteAsync(
            _payload,
            IntentType.Bus,
            _options,
            delayTime: null,
            static (_, _, _) => Task.CompletedTask,
            cancellationToken: CancellationToken.None
        );
    }

    private static PublishOptions _CreateOptions(int headerCount)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var i = 0; i < headerCount; i++)
        {
            headers[string.Create(CultureInfo.InvariantCulture, $"header-{i}")] = string.Create(
                CultureInfo.InvariantCulture,
                $"value-{i}"
            );
        }

        return new PublishOptions { Headers = headers };
    }
}
