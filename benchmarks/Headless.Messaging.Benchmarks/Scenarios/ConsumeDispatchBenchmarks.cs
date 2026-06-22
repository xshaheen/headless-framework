// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using BenchmarkDotNet.Attributes;
using Headless.Messaging.Benchmarks.Support;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Measures the per-message consume dispatch path (<see cref="ConsumeMiddlewarePipeline.ExecuteAsync"/>).
/// Each call exercises the three audit findings on this path:
/// <list type="bullet">
/// <item>F-2 — middleware resolution (<c>_ResolveMiddleware</c>): MakeGenericType + GetServices + LINQ per call.</item>
/// <item>F-3 — reflection dispatch fallback (<c>_DispatchAsync</c>): the descriptor carries no HandlerId, so
/// dispatch goes through <c>MakeGenericMethod(...).Invoke(...)</c> per call.</item>
/// <item>F-14 — header copy: <c>new MessageHeader(originHeaders)</c> clones the header dictionary per call.</item>
/// </list>
/// Vary <see cref="MiddlewareCount"/> to characterize the F-2 component.
/// </summary>
[MemoryDiagnoser]
public class ConsumeDispatchBenchmarks
{
    private ServiceProvider _provider = null!;
    private IConsumeMiddlewarePipeline _pipeline = null!;
    private ConsumerContext _context = null!;
    private BenchmarkPayload _payload = null!;

    [Params(0, 1, 5)]
    public int MiddlewareCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageDispatcher>(new NoOpMessageDispatcher());

        for (var i = 0; i < MiddlewareCount; i++)
        {
            services.AddScoped<IConsumeMiddleware<ConsumeContext>, NoOpConsumeMiddleware>();
        }

        _provider = services.BuildServiceProvider();
        _pipeline = new ConsumeMiddlewarePipeline(_provider, EmptyRuntimeConsumerRegistry.Instance);
        _payload = new BenchmarkPayload("payload");
        _context = _BuildConsumerContext(_payload);

        // Warm up: the pipeline compiles and caches a per-message-type ConsumeContext factory on first use.
        // Run one dispatch in setup so that one-time compile cost is excluded from the measured path.
        _pipeline
            .ExecuteAsync(_BuildConsumerContext(_payload), _payload, typeof(BenchmarkPayload), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public Task ExecuteDispatch()
    {
        return _pipeline.ExecuteAsync(_context, _payload, typeof(BenchmarkPayload), CancellationToken.None);
    }

    private static ConsumerContext _BuildConsumerContext(BenchmarkPayload payload)
    {
        var descriptor = new ConsumerExecutorDescriptor
        {
            IntentType = IntentType.Bus,
            MethodInfo = typeof(ConsumeDispatchBenchmarks).GetMethod(
                nameof(ExecuteDispatch),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            )!,
            ImplTypeInfo = typeof(ConsumeDispatchBenchmarks).GetTypeInfo(),
            MessageName = "benchmark.payload",
            GroupName = "benchmark-group",
        };

        var origin = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = "msg-1",
                [Headers.MessageName] = "benchmark.payload",
            },
            payload
        );

        return new ConsumerContext(
            descriptor,
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = origin,
                Content = "{}",
                IntentType = IntentType.Bus,
                Added = DateTime.UtcNow,
            }
        );
    }
}
