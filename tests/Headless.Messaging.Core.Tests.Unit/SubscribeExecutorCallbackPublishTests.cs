// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed record CallbackOrderShipped(string OrderId);

/// <summary>
/// Where a consumer's callback response is written. The transactional tier owes the response the handler's own
/// transaction — it must vanish with a rollback — while the non-transactional tier owes it an autonomous write
/// that outlives the dispatch scope. Asserted against real in-memory storage rows, because a substituted
/// publisher cannot express which of the two a publish took.
/// </summary>
public sealed class SubscribeExecutorCallbackPublishTests : TestBase
{
    private const string _CallbackName = "callbacks.order-shipped";

    #region Test infrastructure

    private sealed class NonRelationalResource : IUnitOfWorkResource
    {
        public bool IsOwned => true;

        public bool IsTransactionCompleted { get; private set; }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            IsTransactionCompleted = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            IsTransactionCompleted = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for the EF inbox runners: it opens the attempt scope's unit of work around the handler, so
    /// whatever the handler publishes through that unit joins the attempt's transaction. Rolling back models a
    /// commit that never landed (a stale fence, a failed commit) after the handler already published.
    /// </summary>
    private sealed class FakeInboxTransactionRunner(IUnitOfWorkManager unitOfWorkManager) : IInboxTransactionRunner
    {
        public bool RollbackAfterHandler { get; set; }

        public bool HandlerCompleted { get; private set; }

        public async Task ExecuteAsync(
            MediumMessage message,
            Func<CancellationToken, Task> handler,
            CancellationToken cancellationToken
        )
        {
            await using var unitOfWork = await unitOfWorkManager.BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(new NonRelationalResource()),
                options: null,
                cancellationToken
            );

            await handler(cancellationToken).ConfigureAwait(false);
            HandlerCompleted = true;

            if (RollbackAfterHandler)
            {
                await unitOfWork.RollbackAsync().ConfigureAwait(false);

                throw new InvalidOperationException("The attempt's transaction did not commit.");
            }

            await unitOfWork.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Counts the scopes a consumer asks the provider for, so the non-transactional callback path can be shown
    /// to create none — it publishes through the singleton bus and needs no scope of its own.
    /// </summary>
    private sealed class ScopeCountingServiceProvider(IServiceProvider inner) : IServiceProvider
    {
        public int ScopesCreated { get; private set; }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IServiceScopeFactory)
                ? new CountingScopeFactory(inner.GetRequiredService<IServiceScopeFactory>(), this)
                : inner.GetService(serviceType);
        }

        private sealed class CountingScopeFactory(IServiceScopeFactory inner, ScopeCountingServiceProvider owner)
            : IServiceScopeFactory
        {
            public IServiceScope CreateScope()
            {
                owner.ScopesCreated++;
                return inner.CreateScope();
            }
        }
    }

    private static ServiceProvider _BuildMessagingHost(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static SubscribeExecutor _CreateExecutor(
        IServiceProvider provider,
        ISubscribeInvoker invoker,
        MessagingOptions options
    )
    {
        var storage = Substitute.For<IDataStorage>();
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        return new SubscribeExecutor(
            provider,
            storage,
            invoker,
            TimeProvider.System,
            provider.GetRequiredService<ILogger<SubscribeExecutor>>(),
            Options.Create(options)
        );
    }

    private static ISubscribeInvoker _InvokerReturningCallback(bool inScope)
    {
        var invoker = Substitute.For<ISubscribeInvoker>();
        var result = new ConsumerExecutedResult(
            new CallbackOrderShipped("order-1"),
            typeof(CallbackOrderShipped),
            Guid.NewGuid().ToString(),
            _CallbackName,
            null
        );

        if (inScope)
        {
            invoker
                .InvokeInScopeAsync(
                    Arg.Any<ConsumerContext>(),
                    Arg.Any<IServiceProvider>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromResult(result));
        }
        else
        {
            invoker
                .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(result));
        }

        return invoker;
    }

    private static async Task<IReadOnlyList<MessageView>> _PublishedRowsAsync(IServiceProvider host)
    {
        var page = await host.GetRequiredService<IDataStorage>()
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    CurrentPage = 0,
                    PageSize = 20,
                },
                AbortToken
            );

        return page.Items;
    }

    private static MediumMessage _CreateMediumMessage()
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.messageName",
            [Headers.Group] = "test-group",
        };

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, "{}"),
            Content = "{}",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
        };
    }

    private static MediumMessage _CreateTransactionalMessage()
    {
        var message = _CreateMediumMessage();
        message.InboxKey = new InboxKey(
            TenantId: null,
            message.Origin.Id,
            MessageLane.Bus,
            "test.messageName",
            "1",
            "tests.subscribe-callback",
            Generation: 0
        );

        return message;
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var consumeMethod = typeof(IConsume<CallbackOrderShipped>).GetMethod(
            nameof(IConsume<>.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            [typeof(ConsumeContext<CallbackOrderShipped>), typeof(CancellationToken)],
            null
        )!;

        return new ConsumerExecutorDescriptor
        {
            Lane = MessageLane.Bus,
            ServiceTypeInfo = typeof(CallbackOrderShippedConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(CallbackOrderShippedConsumer).GetTypeInfo(),
            MethodInfo = consumeMethod,
            MessageName = "test.messageName",
            GroupName = "test-group",
            ConsumerIdentity = "tests.subscribe-callback",
            MessageContractVersion = "1",
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
        };
    }

    #endregion

    [Fact]
    public async Task transactional_tier_callback_response_should_be_discarded_when_the_attempt_rolls_back()
    {
        // given — the attempt's transaction opens around the handler and never commits
        FakeInboxTransactionRunner? runner = null;
        await using var host = _BuildMessagingHost(services =>
            services.AddScoped<IInboxTransactionRunner>(sp =>
                runner = new FakeInboxTransactionRunner(sp.GetRequiredService<IUnitOfWorkManager>())
                {
                    RollbackAfterHandler = true,
                }
            )
        );
        var executor = _CreateExecutor(
            host,
            _InvokerReturningCallback(inScope: true),
            new MessagingOptions { RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional }
        );

        // when
        var result = await executor.ExecuteAsync(_CreateTransactionalMessage(), host, _CreateDescriptor(), AbortToken);

        // then — the response was published inside the handler, and the rollback took it with the attempt
        result.Succeeded.Should().BeFalse();
        runner.Should().NotBeNull();
        runner!.HandlerCompleted.Should().BeTrue("the callback publish must have completed before the rollback");
        (await _PublishedRowsAsync(host)).Should().BeEmpty();
    }

    [Fact]
    public async Task transactional_tier_callback_response_should_be_durable_when_the_attempt_commits()
    {
        // given — the same path, committed: the control that keeps the rollback case honest
        await using var host = _BuildMessagingHost(services =>
            services.AddScoped<IInboxTransactionRunner>(sp => new FakeInboxTransactionRunner(
                sp.GetRequiredService<IUnitOfWorkManager>()
            ))
        );
        var executor = _CreateExecutor(
            host,
            _InvokerReturningCallback(inScope: true),
            new MessagingOptions { RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional }
        );

        // when
        var result = await executor.ExecuteAsync(_CreateTransactionalMessage(), host, _CreateDescriptor(), AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        var rows = await _PublishedRowsAsync(host);
        rows.Select(row => row.Name).Should().Equal(_CallbackName);
        rows[0].IsCoordinated.Should().BeTrue();
    }

    [Fact]
    public async Task non_transactional_tier_callback_response_should_be_written_autonomously_without_a_scope()
    {
        // given — no unit of work is bound on this tier, so the response is an autonomous durable write
        await using var host = _BuildMessagingHost();
        var countingProvider = new ScopeCountingServiceProvider(host);
        var executor = _CreateExecutor(
            countingProvider,
            _InvokerReturningCallback(inScope: false),
            new MessagingOptions { RequiredInboxCapability = MessagingInboxCapabilityTier.ProcessLocal }
        );

        // when — no inbox key and a non-transactional tier: the executor invokes the consumer without a scope
        var result = await executor.ExecuteAsync(_CreateMediumMessage(), host, _CreateDescriptor(), AbortToken);

        // then — the row stands on its own, and the publish needed no DI scope of its own
        result.Succeeded.Should().BeTrue();
        var rows = await _PublishedRowsAsync(host);
        rows.Select(row => row.Name).Should().Equal(_CallbackName);
        rows[0].IsCoordinated.Should().BeFalse();
        countingProvider.ScopesCreated.Should().Be(0);
    }
}

public sealed class CallbackOrderShippedConsumer : IConsume<CallbackOrderShipped>
{
    public ValueTask ConsumeAsync(ConsumeContext<CallbackOrderShipped> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
