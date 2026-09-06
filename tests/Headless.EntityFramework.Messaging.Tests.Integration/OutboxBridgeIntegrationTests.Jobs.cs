// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

public sealed partial class OutboxBridgeIntegrationTests
{
    [Fact]
    public async Task should_not_treat_a_durable_messaging_delay_as_atomic_jobs_enlistment()
    {
        await using var provider = await _BuildProviderAsync(includeJobs: true);
        await _CreateJobsSchemaAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        var marker = $"delayed-deadline-{Guid.NewGuid():N}";
        var delay = TimeSpan.FromHours(1);
        var beforePublish = DateTimeOffset.UtcNow;
        await scope
            .ServiceProvider.GetRequiredService<IBus>()
            .PublishAsync(
                new OrderShipped(marker),
                new PublishOptions
                {
                    DeliveryMode = DeliveryMode.Durable,
                    Delay = delay,
                    MessageId = marker,
                },
                AbortToken
            );
        var published = (await _ReadPublishedAsync(provider, marker)).Should().ContainSingle().Subject;
        published.Message.Headers[Headers.DelayTime].Should().Be(delay.ToString());

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """SELECT "StatusName", "ExpiresAt" FROM messaging."published" WHERE "MessageId" = @id""";
            command.Parameters.AddWithValue("id", marker);
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            (await reader.ReadAsync(AbortToken)).Should().BeTrue();
            reader.GetString(0).Should().Be("Delayed");
            reader.GetFieldValue<DateTimeOffset>(1).Should().BeAfter(beforePublish);
        }

        var schedule = async () =>
            await scope
                .ServiceProvider.GetRequiredService<IJobScheduler>()
                .ScheduleKeyedAsync(
                    new JobKey(marker),
                    DeadlineRegistration.Descriptor,
                    provider.GetRequiredService<DeadlineEvidence>().Due,
                    new Headless.Jobs.Models.JobOptions { RequireAtomicEnlistment = true },
                    AbortToken
                );
        await schedule.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active commit coordinator*");
        (await _ReadDeadlineRowsAsync(provider, marker)).Should().BeEmpty();
        (await _ReadPublishedAsync(provider, marker)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_compose_consumed_facts_outbox_and_atomic_keyed_deadline(bool retryProducer)
    {
        var fault = new OutboxFault { FailuresRemaining = retryProducer ? 1 : 0 };
        await using var provider = await _BuildProviderAsync(
            services => _AddFaultingDispatcher(services, fault),
            options => options.ReplaceService<IExecutionStrategyFactory, RetryOnceStrategyFactory>(),
            includeJobs: true
        );
        await _CreateJobsSchemaAsync(provider);
        var evidence = provider.GetRequiredService<EmissionEvidence>();
        var deadline = provider.GetRequiredService<DeadlineEvidence>();
        var incomingId = Guid.NewGuid().ToString("N");
        var incoming = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = incomingId,
                [Headers.MessageName] = "orders.ship",
                [Headers.ContractVersion] = "2",
                [Headers.CorrelationId] = "composition-root",
                [Headers.TenantId] = "composition-tenant",
            },
            new ShipOrder("composition")
        );

        using var incomingTrace = new Activity("composition-incoming").Start();
        await _InvokeStoredConsumerAsync<ShipOrder, ShipOrderConsumer>(provider, incoming);
        evidence.Parent.Should().NotBeNull();
        evidence.Parent.CausationId.Should().Be(incomingId);
        evidence.Parent.CorrelationId.Should().Be("composition-root");
        evidence.LocalHandlerCalls.Should().Be(1);
        evidence.Children.Should().HaveCount(2);
        fault.Attempts.Should().HaveCount(retryProducer ? 2 : 1);
        if (retryProducer)
        {
            fault.Attempts[0].Should().Equal(fault.Attempts[1]);
        }
        var published = await _ReadPublishedAsync(provider, "evt-derived");
        published.Should().HaveCount(2);
        foreach (var occurrence in evidence.Children)
        {
            _AssertOccurrence(published.Single(row => row.Id == occurrence.EventId), occurrence);
            occurrence.CausationId.Should().Be(evidence.Parent.EventId);
        }

        // The second consumer receives the exact deserialized durable outbox envelope. Broker delivery is covered
        // by transport conformance; this test owns application, occurrence, consumer, and deadline composition.
        var stored = published.Single(row => row.Message.Headers[Headers.MessageName] == "orders.shipped");
        deadline.FailAfterWrite = true;
        var failedDelivery = async () =>
            await _InvokeStoredConsumerAsync<OrderShipped, DeadlineConsumer>(provider, stored.Message);
        var failure = await failedDelivery.Should().ThrowAsync<Exception>();
        failure.Which.ToString().Should().Contain(nameof(DeadlineWriteFailure));
        (await _ReadDeadlineRowsAsync(provider, stored.Id)).Should().BeEmpty();
        (await _CountDeadlineReceiptsAsync(provider, stored.Id)).Should().Be(0);
        (await _CountOrdersAsync()).Should().Be(1);
        (await _ReadPublishedAsync(provider, "evt-derived")).Should().HaveCount(2);

        deadline.FailAfterWrite = false;
        using var retryTrace = new Activity("composition-redelivery")
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom())
            .Start();
        await _InvokeStoredConsumerAsync<OrderShipped, DeadlineConsumer>(provider, stored.Message);
        var committed = (await _ReadDeadlineRowsAsync(provider, stored.Id)).Should().ContainSingle().Subject;
        committed.Generation.Should().Be(1);
        committed.IsCurrentGeneration.Should().BeTrue();
        committed.ContractVersion.Should().Be("4");
        committed.CorrelationId.Should().Be("composition-root");
        committed.CausationId.Should().Be(stored.Id);
        committed.TenantId.Should().Be("composition-tenant");
        committed.CausationId.Should().NotBe(retryTrace.TraceId.ToString());
        (await _CountDeadlineReceiptsAsync(provider, stored.Id)).Should().Be(1);

        await _InvokeStoredConsumerAsync<OrderShipped, DeadlineConsumer>(provider, stored.Message);
        var observed = (await _ReadDeadlineRowsAsync(provider, stored.Id)).Should().ContainSingle().Subject;
        observed.Id.Should().Be(committed.Id);
        observed.Generation.Should().Be(1);
        (await _CountDeadlineReceiptsAsync(provider, stored.Id)).Should().Be(1);
        deadline
            .Results.Select(result => result.Disposition)
            .Should()
            .Equal(JobScheduleDisposition.Created, JobScheduleDisposition.Created, JobScheduleDisposition.Existing);
        deadline.Results.Should().OnlyContain(result => result.IsProvisional);
    }

    private void _RegisterJobs(IServiceCollection services)
    {
        _ = DeadlineRegistration.Descriptor;
        services.AddHeadlessCoordination(setup => setup.UsePostgreSql(fixture.ConnectionString));
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.UseEntityFramework(ef =>
                ef.UseJobsDbContext<JobsDbContext>(db => db.UseNpgsql(fixture.ConnectionString), schema: "jobs")
            );
        });
        services.AddSingleton<DeadlineEvidence>();
    }

    private async Task _CreateJobsSchemaAsync(IServiceProvider provider)
    {
        await using var context = await provider
            .GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        try
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.DuplicateTable)
        {
            // Another theory case already created this collection's Jobs schema.
        }
    }

    private async Task<TimeJobEntity[]> _ReadDeadlineRowsAsync(IServiceProvider provider, string key)
    {
        await using var context = await provider
            .GetRequiredService<IDbContextFactory<JobsDbContext>>()
            .CreateDbContextAsync(AbortToken);
        return await context
            .Set<TimeJobEntity>()
            .AsNoTracking()
            .Where(row => row.BusinessKey == key)
            .ToArrayAsync(AbortToken);
    }

    private async Task<int> _CountDeadlineReceiptsAsync(IServiceProvider provider, string id)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<BridgeTestDbContext>()
            .DeadlineReceipts.CountAsync(row => row.Id == id, AbortToken);
    }

    private async Task _InvokeStoredConsumerAsync<TMessage, TConsumer>(IServiceProvider provider, Message message)
        where TMessage : class
        where TConsumer : IConsume<TMessage>
    {
        var method = typeof(IConsume<TMessage>).GetMethod(
            nameof(IConsume<TMessage>.ConsumeAsync),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ConsumeContext<TMessage>), typeof(CancellationToken)]
        )!;
        var descriptor = new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(TConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(TConsumer).GetTypeInfo(),
            MethodInfo = method,
            MessageName = message.Headers[Headers.MessageName]!,
            GroupName = "bridge-test",
            Lane = MessageLane.Bus,
            MessageContractVersion = message.Headers[Headers.ContractVersion]!,
            Parameters = method
                .GetParameters()
                .Select(parameter => new ParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    IsFromMessaging = parameter.ParameterType == typeof(CancellationToken),
                })
                .ToArray(),
        };
        var medium = new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = message,
            Lane = MessageLane.Bus,
            Content = provider.GetRequiredService<ISerializer>().Serialize(message),
        };
        await provider
            .GetRequiredService<ISubscribeInvoker>()
            .InvokeAsync(new ConsumerContext(descriptor, medium), AbortToken);
    }

    private sealed class DeadlineReceipt
    {
        public required string Id { get; init; }
    }

    private sealed class DeadlineEvidence
    {
        public bool FailAfterWrite { get; set; }
        public DateTimeOffset Due { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public List<JobScheduleResult> Results { get; } = [];
    }

    private sealed class DeadlineWriteFailure : Exception;

    private sealed class DeadlineConsumer(BridgeTestDbContext db, IJobScheduler scheduler, DeadlineEvidence evidence)
        : IConsume<OrderShipped>
    {
        public async ValueTask ConsumeAsync(ConsumeContext<OrderShipped> context, CancellationToken cancellationToken)
        {
            await db.ExecuteCoordinatedTransactionAsync(
                async (caller, token) =>
                {
                    if (!await caller.DeadlineReceipts.AnyAsync(row => row.Id == context.MessageId, token))
                    {
                        caller.DeadlineReceipts.Add(new DeadlineReceipt { Id = context.MessageId });
                        await caller.SaveChangesAsync(token);
                    }
                    evidence.Results.Add(
                        await scheduler.ScheduleKeyedAsync(
                            new JobKey(context.MessageId),
                            DeadlineRegistration.Descriptor,
                            evidence.Due,
                            new Headless.Jobs.Models.JobOptions
                            {
                                RequireAtomicEnlistment = true,
                                CorrelationId = context.CorrelationId,
                                CausationId = context.MessageId,
                                TenantId = context.TenantId,
                            },
                            token
                        )
                    );
                    if (evidence.FailAfterWrite)
                    {
                        throw new DeadlineWriteFailure();
                    }
                },
                cancellationToken: cancellationToken
            );
        }
    }

    private static class DeadlineRegistration
    {
        public static readonly JobFunctionDescriptor Descriptor = new(
            "composition.deadline",
            null,
            string.Empty,
            JobPriority.Normal,
            1,
            "4"
        );

        static DeadlineRegistration()
        {
            JobFunctionProvider.RegisterFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [Descriptor.FunctionName] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.Normal,
                        MaxConcurrency = 1,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                    },
                }
            );
            JobFunctionProvider.RegisterDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [Descriptor.FunctionName] = Descriptor,
                }
            );
        }
    }
}
