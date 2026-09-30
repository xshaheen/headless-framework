// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

public sealed record TenantTelemetryProbe(string Value);

/// <summary>
/// The transactional-inbox executor switches the tenant from the envelope before the consume middleware runs, and
/// the tenant propagation middleware then switches it again. Driven through the real executor, invoker, and
/// middleware pipeline, the consumer must see exactly one <c>TenantId</c> logging scope: a second one would repeat
/// the property on every log record the handler writes.
/// </summary>
public sealed class TransactionalInboxTenantTelemetryTests : TestBase
{
    private const string _MessageName = "tests.tenant-telemetry.probe";
    private const string _Group = "tenant-telemetry-group";

    [Fact]
    public async Task consumer_on_transactional_inbox_path_sees_one_tenant_scope()
    {
        // given
        using var scopes = new SharedScopeLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(scopes);
        builder.Services.AddHeadlessMessaging(messaging =>
        {
            messaging.UseInMemory();
            messaging.UseInMemoryStorage();
            messaging.Bus.ForMessage<TenantTelemetryProbe>(message =>
                message
                    .Contract(_MessageName)
                    .Consumer<TenantTelemetryProbeConsumer>(consumer =>
                        consumer.StableContract("tests.tenant-telemetry").Group(_Group)
                    )
            );
        });
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        builder.Services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddSingleton(scopes);
        builder.Services.AddSingleton<ConsumerObservation>();
        builder.Services.AddScoped<IInboxTransactionRunner, PassThroughInboxTransactionRunner>();
        await using var host = builder.Services.BuildServiceProvider();

        var descriptor = host.GetRequiredService<IConsumerServiceSelector>().SelectCandidates().Single();
        var executor = _CreateExecutor(host);

        // when
        var result = await executor.ExecuteAsync(_CreateTenantMessage("acme"), host, descriptor, AbortToken);

        // then
        result.Succeeded.Should().BeTrue();
        var observation = host.GetRequiredService<ConsumerObservation>();
        observation.TenantId.Should().Be("acme");
        observation
            .ScopeProperties.Where(property =>
                string.Equals(
                    property.Key,
                    TenantTelemetryOptions.DefaultLogScopePropertyName,
                    StringComparison.Ordinal
                )
            )
            .Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be("acme");
        scopes.ActiveProperties().Should().BeEmpty();
    }

    private static SubscribeExecutor _CreateExecutor(IServiceProvider host)
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
            host,
            storage,
            host.GetRequiredService<ISubscribeInvoker>(),
            TimeProvider.System,
            host.GetRequiredService<ILogger<SubscribeExecutor>>(),
            Options.Create(
                new MessagingOptions { RequiredInboxCapability = MessagingInboxCapabilityTier.Transactional }
            )
        );
    }

    private static MediumMessage _CreateTenantMessage(string tenantId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = _MessageName,
            [Headers.Group] = _Group,
            [Headers.TenantId] = tenantId,
        };
        var message = new Message(headers, """{"Value":"probe"}""");

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = message,
            Content = """{"Value":"probe"}""",
            Lane = MessageLane.Bus,
            Added = DateTimeOffset.UtcNow,
            InboxKey = new InboxKey(
                tenantId,
                message.Id,
                MessageLane.Bus,
                _MessageName,
                "1",
                "tests.tenant-telemetry",
                Generation: 0
            ),
        };
    }

    public sealed class ConsumerObservation
    {
        public string? TenantId { get; set; }

        public IReadOnlyList<KeyValuePair<string, object?>> ScopeProperties { get; set; } = [];
    }

    public sealed class TenantTelemetryProbeConsumer(
        ICurrentTenant currentTenant,
        SharedScopeLoggerProvider scopes,
        ConsumerObservation observation
    ) : IConsume<TenantTelemetryProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TenantTelemetryProbe> context, CancellationToken cancellationToken)
        {
            observation.TenantId = currentTenant.Id;
            observation.ScopeProperties = scopes.ActiveProperties();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Receives the logger factory's shared scope provider, so the scopes it reports are the ones every logger of
    /// the host (the executor's and the middleware's) opened on the calling async flow.
    /// </summary>
    public sealed class SharedScopeLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopeProvider;

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public IReadOnlyList<KeyValuePair<string, object?>> ActiveProperties()
        {
            var properties = new List<KeyValuePair<string, object?>>();
            _scopeProvider?.ForEachScope(
                static (scope, list) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        list.AddRange(pairs);
                    }
                },
                properties
            );

            return properties;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose() { }
    }

    private sealed class PassThroughInboxTransactionRunner(IUnitOfWorkFactory unitOfWorkFactory)
        : IInboxTransactionRunner
    {
        public async Task ExecuteAsync(
            MediumMessage message,
            Func<IUnitOfWork, CancellationToken, Task> handler,
            CancellationToken cancellationToken
        )
        {
            await using var unitOfWork = await unitOfWorkFactory.BeginAsync(
                _ => ValueTask.FromResult<IUnitOfWorkResource>(new CompletingResource()),
                options: null,
                cancellationToken
            );

            await handler(unitOfWork, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    private sealed class CompletingResource : IUnitOfWorkResource
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
}
