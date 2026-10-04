using System.Diagnostics;
// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests;

public sealed record ConformanceReceiveMessage(string Id, string Content);

public sealed class ConformanceReceiveConsumer : IConsume<ConformanceReceiveMessage>
{
    private static TaskCompletionSource<bool> _consumedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _invocationCount;

    public static int InvocationCount
    {
        get => Volatile.Read(ref _invocationCount);
        set => Volatile.Write(ref _invocationCount, value);
    }

    public static string? LastConsumedId { get; set; }

    public static void Reset()
    {
        _consumedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InvocationCount = 0;
        LastConsumedId = null;
    }

    public static Task<bool> WaitForConsumeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _consumedTcs.Task.WaitAsync(timeout, cancellationToken);

    public ValueTask ConsumeAsync(
        ConsumeContext<ConformanceReceiveMessage> context,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _invocationCount);
        LastConsumedId = context.Message.Id;
        _consumedTcs.TrySetResult(true);
        return ValueTask.CompletedTask;
    }
}

public sealed class ConformanceAcceptReceiveMiddleware : IReceiveMiddleware
{
    public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
    {
        context.SetHeader("x-conformance-verified", "true");
        return next();
    }
}

public sealed class ConformanceSkipReceiveMiddleware : IReceiveMiddleware
{
    public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
    {
        context.Skip("conformance-skip-requested");
        return ValueTask.CompletedTask;
    }
}

public sealed class ConformanceRejectReceiveMiddleware : IReceiveMiddleware
{
    public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
    {
        context.Reject("conformance-reject-requested", new InvalidOperationException("forced-conformance-reject"));
        return ValueTask.CompletedTask;
    }
}

public sealed class ConformanceCancelReceiveMiddleware : IReceiveMiddleware
{
    public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
    {
        var cancelled = new CancellationToken(canceled: true);
        context.SetCancellationToken(cancelled);
        throw new OperationCanceledException(cancelled);
    }
}

[PublicAPI]
public static class ReceiveMiddlewareOutcomeConformance
{
    public static async Task AssertAcceptOutcomeAsync(
        Action<MessagingSetupBuilder> configureTransport,
        Action<MessagingSetupBuilder> configureStorage,
        ILoggerProvider loggerProvider,
        CancellationToken cancellationToken
    )
    {
        ConformanceReceiveConsumer.Reset();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            configureTransport(setup);
            configureStorage(setup);
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.Version = "v1";
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ConformanceReceiveMessage>("conformance.receive.accept");
            messaging.AddModule<ConformanceAcceptReceiveModule>();
        });

        messagingBuilder.AddReceiveMiddleware<ConformanceAcceptReceiveMiddleware>();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messageId = Guid.NewGuid().ToString("N");
        await bus.PublishAsync(
            new ConformanceReceiveMessage(messageId, "payload"),
            new PublishOptions { DeliveryMode = DeliveryMode.Direct },
            cancellationToken
        );

        var consumed = await ConformanceReceiveConsumer.WaitForConsumeAsync(TimeSpan.FromSeconds(5), cancellationToken);
        consumed.Should().BeTrue("Accept outcome must invoke the consumer handler");
        ConformanceReceiveConsumer.InvocationCount.Should().Be(1);
        ConformanceReceiveConsumer.LastConsumedId.Should().Be(messageId);

        var storage = provider.GetRequiredService<IDataStorage>();
        var sw = Stopwatch.StartNew();
        StatisticsView stats;
        do
        {
            stats = await storage.GetMonitoringApi().GetStatisticsAsync(cancellationToken);
            if (stats.ReceivedSucceeded >= 1)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        } while (sw.Elapsed < TimeSpan.FromSeconds(5));

        stats.ReceivedSucceeded.Should().Be(1);
    }

    public static async Task AssertSkipOutcomeAsync(
        Action<MessagingSetupBuilder> configureTransport,
        Action<MessagingSetupBuilder> configureStorage,
        ILoggerProvider loggerProvider,
        CancellationToken cancellationToken
    )
    {
        ConformanceReceiveConsumer.Reset();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            configureTransport(setup);
            configureStorage(setup);
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.Version = "v1";
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ConformanceReceiveMessage>("conformance.receive.skip");
            messaging.AddModule<ConformanceSkipReceiveModule>();
        });

        messagingBuilder.AddReceiveMiddleware<ConformanceSkipReceiveMiddleware>();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messageId = Guid.NewGuid().ToString("N");
        await bus.PublishAsync(
            new ConformanceReceiveMessage(messageId, "payload"),
            new PublishOptions { DeliveryMode = DeliveryMode.Direct },
            cancellationToken
        );

        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        ConformanceReceiveConsumer.InvocationCount.Should().Be(0, "Skip outcome must not invoke consumer");

        var storage = provider.GetRequiredService<IDataStorage>();
        var stats = await storage.GetMonitoringApi().GetStatisticsAsync(cancellationToken);
        stats.ReceivedSucceeded.Should().Be(0);
        stats.ReceivedFailed.Should().Be(0);
    }

    public static async Task AssertRejectOutcomeAsync(
        Action<MessagingSetupBuilder> configureTransport,
        Action<MessagingSetupBuilder> configureStorage,
        ILoggerProvider loggerProvider,
        CancellationToken cancellationToken
    )
    {
        ConformanceReceiveConsumer.Reset();
        var onExhaustedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            configureTransport(setup);
            configureStorage(setup);
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.Version = "v1";
            setup.Options.RetryPolicy.OnExhausted = (_, _) =>
            {
                onExhaustedTcs.TrySetResult(true);
                return Task.CompletedTask;
            };
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ConformanceReceiveMessage>("conformance.receive.reject");
            messaging.AddModule<ConformanceRejectReceiveModule>();
        });

        messagingBuilder.AddReceiveMiddleware<ConformanceRejectReceiveMiddleware>();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messageId = Guid.NewGuid().ToString("N");
        await bus.PublishAsync(
            new ConformanceReceiveMessage(messageId, "payload"),
            new PublishOptions { DeliveryMode = DeliveryMode.Direct },
            cancellationToken
        );

        var exhaustedFired = await onExhaustedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        exhaustedFired.Should().BeTrue("Reject outcome must trigger OnExhausted");
        ConformanceReceiveConsumer.InvocationCount.Should().Be(0, "Reject outcome must not invoke consumer");

        var storage = provider.GetRequiredService<IDataStorage>();
        var sw = Stopwatch.StartNew();
        StatisticsView stats;
        do
        {
            stats = await storage.GetMonitoringApi().GetStatisticsAsync(cancellationToken);
            if (stats.ReceivedFailed >= 1)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        } while (sw.Elapsed < TimeSpan.FromSeconds(5));

        stats.ReceivedFailed.Should().Be(1, "Reject outcome must store a failed exception row");
    }

    public static async Task AssertCancelledOutcomeAsync(
        Action<MessagingSetupBuilder> configureTransport,
        Action<MessagingSetupBuilder> configureStorage,
        ILoggerProvider loggerProvider,
        CancellationToken cancellationToken
    )
    {
        ConformanceReceiveConsumer.Reset();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var messagingBuilder = services.AddHeadlessMessaging(setup =>
        {
            configureTransport(setup);
            configureStorage(setup);
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal;
            setup.Options.Version = "v1";
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ConformanceReceiveMessage>("conformance.receive.cancel");
            messaging.AddModule<ConformanceCancelReceiveModule>();
        });

        messagingBuilder.AddReceiveMiddleware<ConformanceCancelReceiveMiddleware>();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken);

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messageId = Guid.NewGuid().ToString("N");
        await bus.PublishAsync(
            new ConformanceReceiveMessage(messageId, "payload"),
            new PublishOptions { DeliveryMode = DeliveryMode.Direct },
            cancellationToken
        );

        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        ConformanceReceiveConsumer.InvocationCount.Should().Be(0, "Cancelled outcome must not invoke consumer");

        var storage = provider.GetRequiredService<IDataStorage>();
        var stats = await storage.GetMonitoringApi().GetStatisticsAsync(cancellationToken);
        stats.ReceivedSucceeded.Should().Be(0);
        stats.ReceivedFailed.Should().Be(0);
    }
}

public sealed class ConformanceAcceptReceiveModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        ConformanceReceiveModules.Register(catalog, "tests.conformance.accept");
}

public sealed class ConformanceSkipReceiveModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        ConformanceReceiveModules.Register(catalog, "tests.conformance.skip");
}

public sealed class ConformanceRejectReceiveModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        ConformanceReceiveModules.Register(catalog, "tests.conformance.reject");
}

public sealed class ConformanceCancelReceiveModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog) =>
        ConformanceReceiveModules.Register(catalog, "tests.conformance.cancel");
}

// Each outcome runs in its own host under its own identity, so every outcome gets a module of its own.
internal static class ConformanceReceiveModules
{
    public static void Register(MessagingCatalogBuilder catalog, string identity) =>
        catalog.AddBusConsumer<ConformanceReceiveConsumer, ConformanceReceiveMessage>(
            identity,
            everyInstance: false,
            TestConsumerDispatch.FromServices<ConformanceReceiveConsumer, ConformanceReceiveMessage>()
        );
}
