using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.IntegrationTests;

public sealed class SharedConsumeScopeIntegrationTests : TestBase
{
    [Fact]
    public async Task should_use_same_scope_for_class_handler_and_middleware()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var publisher = provider.GetRequiredService<IOutboxBus>();
        var recorder = provider.GetRequiredService<ScopedExecutionRecorder>();

        await publisher.PublishAsync(
            new ScopedMessage("class"),
            new PublishOptions { MessageName = "scope.class" },
            AbortToken
        );

        await recorder.WaitForClassHandlerAsync(AbortToken);

        recorder.MiddlewareScopedIds.Should().ContainSingle();
        recorder.ClassHandlerScopedIds.Should().ContainSingle();
        recorder.MiddlewareScopedIds.Single().Should().Be(recorder.ClassHandlerScopedIds.Single());
    }

    [Fact]
    public async Task should_use_same_scope_for_runtime_handler_and_middleware()
    {
        await using var provider = await _CreateStartedProviderAsync();
        var publisher = provider.GetRequiredService<IOutboxBus>();
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var recorder = provider.GetRequiredService<ScopedExecutionRecorder>();

        await runtimeSubscriber.SubscribeAsync<ScopedMessage>(
            async (_, services, _) =>
            {
                recorder.RecordRuntimeHandler(services.GetRequiredService<ScopedExecutionDependency>().Id);
                await Task.CompletedTask;
            },
            new RuntimeSubscriptionOptions
            {
                MessageName = "scope.runtime",
                Group = "scope.runtime",
                HandlerId = "Tests.IntegrationTests.SharedConsumeScopeIntegrationTests.RuntimeHandler",
            },
            AbortToken
        );

        await publisher.PublishAsync(
            new ScopedMessage("runtime"),
            new PublishOptions { MessageName = "scope.runtime" },
            AbortToken
        );

        await recorder.WaitForRuntimeHandlerAsync(AbortToken);

        recorder.MiddlewareScopedIds.Should().ContainSingle();
        recorder.RuntimeHandlerScopedIds.Should().ContainSingle();
        recorder.MiddlewareScopedIds.Single().Should().Be(recorder.RuntimeHandlerScopedIds.Single());
    }

    private async Task<ServiceProvider> _CreateStartedProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(LoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<ScopedExecutionRecorder>();
        services.AddScoped<ScopedExecutionDependency>();
        services
            .AddHeadlessMessaging(options =>
            {
                options.ForMessage<ScopedMessage>(message =>
                    message
                        .MessageName("scope.class")
                        .OnBus<ScopedClassConsumer>(consumer => consumer.Group("scope.class"))
                );
                options.UseInMemory();
                options.UseInMemoryStorage();
                options.UseConventions(c =>
                {
                    c.UseApplicationId("shared-scope-tests");
                    c.UseVersion("v1");
                });
            })
            .AddBusConsumeMiddleware<ScopedExecutionMiddleware>();

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private sealed record ScopedMessage(string Id);

    private sealed class ScopedExecutionDependency
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class ScopedExecutionRecorder
    {
        private readonly TaskCompletionSource _classHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _runtimeHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<Guid> MiddlewareScopedIds { get; } = [];
        public ConcurrentQueue<Guid> ClassHandlerScopedIds { get; } = [];
        public ConcurrentQueue<Guid> RuntimeHandlerScopedIds { get; } = [];

        public void RecordMiddleware(Guid scopedId)
        {
            MiddlewareScopedIds.Enqueue(scopedId);
        }

        public void RecordClassHandler(Guid scopedId)
        {
            ClassHandlerScopedIds.Enqueue(scopedId);
            _classHandled.TrySetResult();
        }

        public void RecordRuntimeHandler(Guid scopedId)
        {
            RuntimeHandlerScopedIds.Enqueue(scopedId);
            _runtimeHandled.TrySetResult();
        }

        public Task WaitForClassHandlerAsync(CancellationToken cancellationToken)
        {
            return _classHandled.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        public Task WaitForRuntimeHandlerAsync(CancellationToken cancellationToken)
        {
            return _runtimeHandled.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class ScopedExecutionMiddleware(
        ScopedExecutionRecorder recorder,
        ScopedExecutionDependency dependency
    ) : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.RecordMiddleware(dependency.Id);
            await next().ConfigureAwait(false);
        }
    }

    private sealed class ScopedClassConsumer(ScopedExecutionRecorder recorder, ScopedExecutionDependency dependency)
        : IConsume<ScopedMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<ScopedMessage> context, CancellationToken cancellationToken)
        {
            recorder.RecordClassHandler(dependency.Id);
            return ValueTask.CompletedTask;
        }
    }
}
