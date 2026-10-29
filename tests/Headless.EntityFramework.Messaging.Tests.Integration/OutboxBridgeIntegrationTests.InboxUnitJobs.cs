// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

public sealed partial class OutboxBridgeIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_commit_a_job_scheduled_through_the_inbox_unit_only_with_the_attempt(bool handlerFails)
    {
        // given: raw PostgreSQL storage on the default inbox guarantee, and no immediate retry, so one executor call
        // runs one attempt
        var state = new InboxJobsState { HandlerFails = handlerFails };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.ConfigureStorage(storage => storage.Schema = $"inbox_jobs_{Guid.NewGuid():N}");
            setup.UsePostgreSql(fixture.ConnectionString);
            setup.DefaultFailurePolicy(policy => policy.Delayed(1, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<InboxJobsMessage>("tests.inbox-jobs");
            messaging.AddModule<InboxJobsModule>();
        });
        _RegisterJobs(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken);
        await _CreateJobsSchemaAsync(provider);
        var storage = provider.GetRequiredService<IDataStorage>();
        var descriptor = _GetDescriptor<InboxJobsConsumer>(provider);
        var key = Guid.NewGuid().ToString("N");
        var admitted = await storage.AdmitReceivedMessageAsync(
            descriptor.MessageName,
            descriptor.ConsumerIdentity!,
            descriptor.MessageContractVersion!,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = new Message(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [Headers.MessageId] = key,
                        [Headers.MessageName] = descriptor.MessageName,
                        [Headers.ConsumerIdentity] = descriptor.ConsumerIdentity,
                    },
                    new InboxJobsMessage(key)
                ),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken: AbortToken
        );
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);

        // when
        await using var dispatchScope = provider.CreateAsyncScope();
        var result = await provider
            .GetRequiredService<ISubscribeExecutor>()
            .ExecuteAsync(admitted.Message, dispatchScope.ServiceProvider, descriptor, AbortToken);

        // then: the job row exists exactly when the attempt committed
        result.Succeeded.Should().Be(!handlerFails, "{0}", result.Exception);
        state.Scheduled.Should().Be(1, "the handler scheduled the job before deciding the attempt's outcome");
        var rows = await _ReadDeadlineRowsAsync(provider, key);
        if (handlerFails)
        {
            rows.Should().BeEmpty("the job was scheduled in the attempt's transaction, which rolled back");
        }
        else
        {
            rows.Should().ContainSingle();
        }
    }

    internal sealed record InboxJobsMessage(string Key);

    internal sealed class InboxJobsState
    {
        public bool HandlerFails { get; init; }
        public int Scheduled { get; set; }
    }

    private sealed class InboxJobsFailureException : Exception;

    private sealed class InboxJobsModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<InboxJobsConsumer, InboxJobsMessage>(
                "tests.inbox-jobs.consumer",
                everyInstance: false,
                TestConsumerDispatch.FromServices<InboxJobsConsumer, InboxJobsMessage>()
            );
    }

    // Declared through a hand-written module rather than [BusConsumer], so the generated module the other tests
    // register does not pick it up.
    internal sealed class InboxJobsConsumer(DeadlineEvidence evidence, InboxJobsState state)
        : IConsume<InboxJobsMessage>
    {
        public async ValueTask ConsumeAsync(
            ConsumeContext<InboxJobsMessage> context,
            CancellationToken cancellationToken
        )
        {
            await context
                .GetRequiredUnitOfWork()
                .Jobs.ScheduleKeyedAsync<DeadlineJob>(
                    new JobKey(context.Message.Key),
                    evidence.Due,
                    new JobOptions { CausationId = context.MessageId },
                    cancellationToken
                );
            state.Scheduled++;
            if (state.HandlerFails)
            {
                throw new InboxJobsFailureException();
            }
        }
    }
}
