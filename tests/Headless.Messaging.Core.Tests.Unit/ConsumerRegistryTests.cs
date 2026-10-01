using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ConsumerRegistryTests : TestBase
{
    [Fact]
    public void should_register_consumer_metadata()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "test.messageName",
            2,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.register",
            MessageContractVersion: "v1"
        );

        // when
        registry.Register(metadata);
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all.Should().HaveElementAt(0, metadata);
    }

    [Fact]
    public void should_freeze_registry_after_first_getall()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test",
                1,
                MessageLane.Bus,
                "tests.registry.freeze",
                "v1"
            )
        );

        // when
        _ = registry.GetAll(); // Freeze
        var act = () =>
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    "test2",
                    1,
                    MessageLane.Bus,
                    "tests.registry.freeze-late",
                    "v1"
                )
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Cannot register consumers after the registry has been frozen*");
    }

    [Fact]
    public void should_update_existing_metadata()
    {
        // given
        var registry = new ConsumerRegistry();
        var original = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "original",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.update",
            MessageContractVersion: "v1"
        );
        var updated = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "updated",
            5,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.update",
            MessageContractVersion: "v1"
        );
        registry.Register(original);

        // when
        registry.Update(m => m.ConsumerType == typeof(TestConsumer), updated);
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all.Should().HaveElementAt(0, updated);
        all[0].MessageName.Should().Be("updated");
        all[0].Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_not_update_if_predicate_matches_nothing()
    {
        // given
        var registry = new ConsumerRegistry();
        var original = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "original",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.no-match-original",
            MessageContractVersion: "v1"
        );
        registry.Register(original);

        // when
        registry.Update(
            m => m.ConsumerType == typeof(OtherConsumer),
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(OtherConsumer),
                "new",
                1,
                MessageLane.Bus,
                "tests.registry.no-match",
                "v1"
            )
        );
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all.Should().HaveElementAt(0, original);
    }

    [Fact]
    public void should_throw_when_updating_frozen_registry()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test",
                1,
                MessageLane.Bus,
                "tests.registry.frozen-update",
                "v1"
            )
        );
        _ = registry.GetAll(); // Freeze

        // when
        var act = () =>
            registry.Update(
                m => m.ConsumerType == typeof(TestConsumer),
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    "updated",
                    1,
                    Lane: MessageLane.Bus,
                    ConsumerIdentity: "tests.registry.frozen-update-new",
                    MessageContractVersion: "v1"
                )
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Cannot update consumers after the registry has been frozen*");
    }

    [Fact]
    public void should_return_same_readonly_list_on_subsequent_getall_calls()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test",
                1,
                MessageLane.Bus,
                "tests.registry.readonly",
                "v1"
            )
        );

        // when
        var first = registry.GetAll();
        var second = registry.GetAll();

        // then
        ReferenceEquals(first, second).Should().BeTrue("frozen list should be cached");
    }

    [Fact]
    public void should_register_multiple_consumers_sequentially()
    {
        // given
        var registry = new ConsumerRegistry();
        const int consumerCount = 100;

        // when
        for (var i = 1; i <= consumerCount; i++)
        {
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    $"messageName.{i}",
                    (byte)((i % 10) + 1),
                    MessageLane.Bus,
                    $"tests.registry.sequential-registration.{i}",
                    "v1"
                )
            );
        }

        var all = registry.GetAll();

        // then
        all.Should().HaveCount(consumerCount, "all registrations should succeed");
        all.Select(m => m.MessageName)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .HaveCount(consumerCount, "all topics should be unique");
    }

    [Fact]
    public void should_allow_two_bus_consumers_of_one_message_under_different_identities()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "orders.placed",
                1,
                Lane: MessageLane.Bus,
                ConsumerIdentity: "tests.registry.topic-collision.first",
                MessageContractVersion: "v1"
            )
        );

        // when
        var act = () =>
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(OtherConsumer),
                    "orders.placed",
                    1,
                    Lane: MessageLane.Bus,
                    ConsumerIdentity: "tests.registry.topic-collision.second",
                    MessageContractVersion: "v1"
                )
            );

        // then: each Bus identity is its own subscription, so both consumers receive the message.
        act.Should().NotThrow();
    }

    [Fact]
    public void should_reject_updates_that_give_a_queue_message_a_second_consumer()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "orders.placed",
                1,
                Lane: MessageLane.Queue,
                ConsumerIdentity: "tests.registry.update-collision.first",
                MessageContractVersion: "v1"
            )
        );
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(OtherConsumer),
                "orders.cancelled",
                1,
                Lane: MessageLane.Queue,
                ConsumerIdentity: "tests.registry.update-collision.second",
                MessageContractVersion: "v1"
            )
        );

        // when
        var act = () =>
            registry.Update(
                m => m.ConsumerType == typeof(OtherConsumer),
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(OtherConsumer),
                    "orders.placed",
                    1,
                    Lane: MessageLane.Queue,
                    ConsumerIdentity: "tests.registry.update-collision.second",
                    MessageContractVersion: "v1"
                )
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Queue message 'orders.placed' has two consumers*");
    }

    [Fact]
    public async Task should_prevent_registration_after_concurrent_freeze()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test",
                1,
                MessageLane.Bus,
                "tests.registry.concurrent-freeze",
                "v1"
            )
        );

        var freezeTask = Task.Run(() => registry.GetAll());
        InvalidOperationException? caughtException = null;

        var registerTask = Task.Run(
            async () =>
            {
                await Task.Delay(10);
                try
                {
                    registry.Register(
                        new ConsumerMetadata(
                            typeof(TestMessage),
                            typeof(TestConsumer),
                            "test2",
                            1,
                            Lane: MessageLane.Bus,
                            ConsumerIdentity: "tests.registry.concurrent-freeze-late",
                            MessageContractVersion: "v1"
                        )
                    );
                }
                catch (InvalidOperationException ex)
                {
                    caughtException = ex;
                }
            },
            AbortToken
        );

        // when
        await Task.WhenAll(freezeTask, registerTask);

        // then - registration after freeze should throw
        caughtException.Should().NotBeNull("registration after freeze should have thrown");
        caughtException!.Message.Should().Contain("frozen");
    }

    [Fact]
    public void should_allow_sequential_updates_before_freeze()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "original",
                1,
                MessageLane.Bus,
                "tests.registry.sequential-update",
                "v1"
            )
        );
        const int updateCount = 50;

        // when
        for (var i = 1; i <= updateCount; i++)
        {
            registry.Update(
                m => m.ConsumerType == typeof(TestConsumer),
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    $"messageName.{i}",
                    (byte)((i % 10) + 1),
                    MessageLane.Bus,
                    "tests.registry.sequential-update",
                    "v1"
                )
            );
        }

        var all = registry.GetAll();

        // then
        all.Should().ContainSingle("only one consumer registered");
        all[0].MessageName.Should().Be("messageName.50", "last update should win");
        all[0].Concurrency.Should().Be(1, "50 % 10 + 1 = 1");
    }

    [Fact]
    public async Task should_handle_concurrent_registration_and_freeze_without_race()
    {
        // given
        const int iterations = 100;
        const int registrationsPerIteration = 10;
        var exceptions = new ConcurrentBag<Exception>();

        // when - stress test the race condition
        for (var iter = 0; iter < iterations; iter++)
        {
            var registry = new ConsumerRegistry();
            using var barrier = new Barrier(registrationsPerIteration + 1);

            var tasks = new List<Task>();

            // Every participant runs on a dedicated thread: 11 participants blocked in the barrier on pool threads
            // starve the pool (~1 thread injected per second), which stretched these 100 iterations to ~8s.
            // Spawn registration tasks
            for (var i = 0; i < registrationsPerIteration; i++)
            {
                var index = i;
                tasks.Add(
                    Task.Factory.StartNew(
                        () =>
                        {
                            try
                            {
                                barrier.SignalAndWait();
                                registry.Register(
                                    new ConsumerMetadata(
                                        typeof(TestMessage),
                                        typeof(TestConsumer),
                                        $"messageName.{index}",
                                        1,
                                        Lane: MessageLane.Bus,
                                        ConsumerIdentity: $"tests.registry.concurrent-registration.{index}",
                                        MessageContractVersion: "v1"
                                    )
                                );
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        },
                        AbortToken,
                        TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                );
            }

            // Spawn freeze task
            tasks.Add(
                Task.Factory.StartNew(
                    () =>
                    {
                        try
                        {
                            barrier.SignalAndWait();
                            _ = registry.GetAll();
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    },
                    AbortToken,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
            );

            await Task.WhenAll(tasks).WaitAsync(AbortToken);
        }

        // then - no NullReferenceException should occur
        var nullRefExceptions = exceptions.Where(e => e is NullReferenceException).ToList();
        nullRefExceptions.Should().BeEmpty("race condition should be prevented by lock");

        // InvalidOperationException is expected when registration happens after freeze
        var invalidOpExceptions = exceptions.Where(e => e is InvalidOperationException).ToList();
        invalidOpExceptions.Should().AllSatisfy(e => e.Message.Should().Contain("frozen"));
    }

    [Fact]
    public async Task should_handle_concurrent_update_and_freeze_without_race()
    {
        // given
        const int iterations = 100;
        var exceptions = new ConcurrentBag<Exception>();

        // when - stress test the race condition in Update
        for (var iter = 0; iter < iterations; iter++)
        {
            var registry = new ConsumerRegistry();
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    "original",
                    1,
                    Lane: MessageLane.Bus,
                    ConsumerIdentity: "tests.registry.concurrent-update",
                    MessageContractVersion: "v1"
                )
            );
            using var barrier = new Barrier(2);

            var updateTask = Task.Run(
                () =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        registry.Update(
                            m => m.ConsumerType == typeof(TestConsumer),
                            new ConsumerMetadata(
                                typeof(TestMessage),
                                typeof(TestConsumer),
                                "updated",
                                5,
                                Lane: MessageLane.Bus,
                                ConsumerIdentity: "tests.registry.concurrent-update",
                                MessageContractVersion: "v1"
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                },
                AbortToken
            );

            var freezeTask = Task.Run(
                () =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        _ = registry.GetAll();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                },
                AbortToken
            );

            await Task.WhenAll(updateTask, freezeTask);
        }

        // then - no NullReferenceException should occur
        var nullRefExceptions = exceptions.Where(e => e is NullReferenceException).ToList();
        nullRefExceptions.Should().BeEmpty("race condition should be prevented by lock");

        // InvalidOperationException is expected when update happens after freeze
        var invalidOpExceptions = exceptions.Where(e => e is InvalidOperationException).ToList();
        invalidOpExceptions.Should().AllSatisfy(e => e.Message.Should().Contain("frozen"));
    }

    private sealed class TestMessage;

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OtherConsumer : IConsume<TestMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    // Discovery API Tests

    [Fact]
    public void should_find_consumer_by_topic_without_group()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "test.messageName",
            2,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.find-topic",
            MessageContractVersion: "v1"
        );
        registry.Register(metadata);

        // when
        var found = registry.FindByMessageName("test.messageName");

        // then
        found.Should().NotBeNull();
        found.Should().Be(metadata);
    }

    [Fact]
    public void should_find_consumer_by_topic_and_subscription()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata1 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "test.messageName",
            2,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.find-group.first",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(OtherConsumer),
            "test.messageName",
            3,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.find-group.second",
            MessageContractVersion: "v1"
        );
        registry.Register(metadata1);
        registry.Register(metadata2);

        // when
        var found = registry.FindByMessageName("test.messageName", "tests.registry.find-group.second");

        // then
        found.Should().NotBeNull();
        found.Should().Be(metadata2);
        found.ConsumerType.Should().Be<OtherConsumer>();
    }

    [Fact]
    public void should_return_null_when_topic_not_found()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test.messageName",
                1,
                Lane: MessageLane.Bus,
                ConsumerIdentity: "tests.registry.topic-not-found",
                MessageContractVersion: "v1"
            )
        );

        // when
        var found = registry.FindByMessageName("nonexistent.messageName");

        // then
        found.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_topic_found_but_subscription_not_found()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test.messageName",
                1,
                Lane: MessageLane.Bus,
                ConsumerIdentity: "tests.registry.group-not-found",
                MessageContractVersion: "v1"
            )
        );

        // when
        var found = registry.FindByMessageName("test.messageName", "tests.registry.find-group.second");

        // then
        found.Should().BeNull();
    }

    [Fact]
    public void should_find_consumers_by_message_type_generic()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata1 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "topic1",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.type-generic.first",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(OtherConsumer),
            "topic2",
            2,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.type-generic.second",
            MessageContractVersion: "v1"
        );
        var metadata3 = new ConsumerMetadata(
            typeof(OtherMessage),
            typeof(OtherMessageConsumer),
            "topic3",
            3,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.type-generic.third",
            MessageContractVersion: "v1"
        );
        registry.Register(metadata1);
        registry.Register(metadata2);
        registry.Register(metadata3);

        // when
        var found = registry.FindByMessageType<TestMessage>().ToList();

        // then
        found.Should().HaveCount(2);
        found.Should().Contain(metadata1);
        found.Should().Contain(metadata2);
        found.Should().NotContain(metadata3);
    }

    [Fact]
    public void should_find_consumers_by_message_type_non_generic()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata1 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "topic1",
            1,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.type-non-generic.first",
            MessageContractVersion: "v1"
        );
        var metadata2 = new ConsumerMetadata(
            typeof(OtherMessage),
            typeof(OtherMessageConsumer),
            "topic2",
            2,
            Lane: MessageLane.Bus,
            ConsumerIdentity: "tests.registry.type-non-generic.second",
            MessageContractVersion: "v1"
        );
        registry.Register(metadata1);
        registry.Register(metadata2);

        // when
        var found = registry.FindByMessageType<OtherMessage>().ToList();

        // then
        found.Should().ContainSingle();
        found.Should().Contain(metadata2);
    }

    [Fact]
    public void should_return_empty_when_message_type_not_found()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "topic1",
                1,
                MessageLane.Bus,
                "tests.registry.missing-type",
                "v1"
            )
        );

        // when
        var found = registry.FindByMessageType<OtherMessage>().ToList();

        // then
        found.Should().BeEmpty();
    }

    [Fact]
    public void should_implement_iconsumer_registry()
    {
        // given
        var registry = new ConsumerRegistry();

        // then
        registry.Should().BeAssignableTo<IConsumerRegistry>();
    }

    [Fact]
    public void should_register_and_lookup_raw_message_name_mapping()
    {
        // given
        var registry = new ConsumerRegistry();

        // when
        registry.RegisterMessageName(typeof(TestMessage), "orders.created");

        // then
        registry.TryGetRawMessageName(typeof(TestMessage), out var messageName).Should().BeTrue();
        messageName.Should().Be("orders.created");
    }

    [Fact]
    public void should_reject_conflicting_message_name_mapping_for_same_type()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "orders.created");

        // when
        var act = () => registry.RegisterMessageName(typeof(TestMessage), "orders.renamed");

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*orders.created*orders.renamed*");
    }

    [Fact]
    public void should_allow_identical_message_name_mapping_for_same_type()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "orders.created");

        // when
        var act = () => registry.RegisterMessageName(typeof(TestMessage), "orders.created");

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_return_false_when_message_name_mapping_is_missing()
    {
        // given
        var registry = new ConsumerRegistry();

        // when
        var found = registry.TryGetRawMessageName(typeof(TestMessage), out var messageName);

        // then
        found.Should().BeFalse();
        messageName.Should().BeNull();
    }

    [Fact]
    public void should_reject_message_name_mapping_after_freeze()
    {
        // given
        var registry = new ConsumerRegistry();
        _ = registry.GetAll();

        // when
        var act = () => registry.RegisterMessageName(typeof(TestMessage), "orders.created");

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Cannot register message-name mappings after the registry has been frozen*");
    }

    [Fact]
    public void lane_specific_message_names_coexist_and_override_the_global_fallback()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "orders.global");
        registry.RegisterMessageName(typeof(TestMessage), MessageLane.Bus, "orders.bus");

        // when
        var hasBus = registry.TryGetRawMessageName(typeof(TestMessage), MessageLane.Bus, out var busName);
        var hasQueue = registry.TryGetRawMessageName(typeof(TestMessage), MessageLane.Queue, out var queueName);

        // then
        hasBus.Should().BeTrue();
        busName.Should().Be("orders.bus");
        hasQueue.Should().BeTrue();
        queueName.Should().Be("orders.global");
    }

    [Fact]
    public void same_lane_names_are_case_insensitive_but_opposite_lane_is_independent()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), MessageLane.Bus, "orders.placed");
        registry.RegisterMessageName(typeof(TestMessage), MessageLane.Queue, "orders.queue");

        // when
        var sameLaneCaseVariant = () =>
            registry.RegisterMessageName(typeof(TestMessage), MessageLane.Bus, "Orders.Placed");
        var sameLaneConflict = () =>
            registry.RegisterMessageName(typeof(TestMessage), MessageLane.Bus, "orders.renamed");

        // then
        sameLaneCaseVariant.Should().NotThrow();
        sameLaneConflict.Should().Throw<InvalidOperationException>();
        registry.TryGetRawMessageName(typeof(TestMessage), MessageLane.Queue, out var queueName).Should().BeTrue();
        queueName.Should().Be("orders.queue");
    }

    [Fact]
    public void same_durable_identity_and_contract_version_are_independent_across_lanes()
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.bus"));

        var act = () => registry.Register(_DurableMetadata(MessageLane.Queue, "orders.queue"));

        act.Should().NotThrow();
    }

    [Fact]
    public void same_lane_identity_message_and_contract_version_collide_independent_of_group()
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.created"));

        var act = () => registry.Register(_DurableMetadata(MessageLane.Bus, "Orders.Created"));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*durable consumer identity*orders-projection*Bus*Orders.Created*v1*");
    }

    [Fact]
    public void one_identity_may_cover_several_messages_of_one_consumer_class()
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.created"));

        var act = () => registry.Register(_DurableMetadata(MessageLane.Bus, "orders.renamed"));

        act.Should().NotThrow();
        registry.GetAll().Select(static x => x.MessageName).Should().Equal("orders.created", "orders.renamed");
    }

    [Fact]
    public void one_identity_on_two_consumer_classes_fails_naming_both_classes_and_modules()
    {
        var registry = new ConsumerRegistry();
        registry.Register(
            _DurableMetadata(MessageLane.Bus, "orders.created") with
            {
                DeclaringModule = "Orders.MessagingModule",
            }
        );

        var act = () =>
            registry.Register(
                _DurableMetadata(MessageLane.Bus, "orders.shipped") with
                {
                    ConsumerType = typeof(OtherMessageConsumer),
                    DeclaringModule = "Billing.MessagingModule",
                }
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*'orders-projection'*declared by two consumer classes*TestConsumer in Orders.MessagingModule*"
                    + "OtherMessageConsumer in Billing.MessagingModule*"
            );
    }

    [Fact]
    public void one_identity_on_two_consumer_classes_is_independent_across_lanes()
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.created"));

        var act = () =>
            registry.Register(
                _DurableMetadata(MessageLane.Queue, "orders.shipped") with
                {
                    ConsumerType = typeof(OtherMessageConsumer),
                }
            );

        act.Should().NotThrow();
    }

    [Fact]
    public void second_declared_queue_consumer_for_one_message_fails_naming_both()
    {
        var registry = new ConsumerRegistry();
        registry.Register(
            _DeclaredQueueMetadata("orders.issue-invoice", typeof(TestConsumer), "Orders.MessagingModule")
        );

        var act = () =>
            registry.Register(
                _DeclaredQueueMetadata("billing.issue-invoice", typeof(OtherMessageConsumer), "Billing.MessagingModule")
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Queue message 'orders.issue-invoice-command' has two consumers: 'orders.issue-invoice'*"
                    + "Orders.MessagingModule*'billing.issue-invoice'*Billing.MessagingModule*at most one Queue consumer*"
            );
    }

    [Fact]
    public void declared_queue_consumers_for_different_messages_coexist()
    {
        var registry = new ConsumerRegistry();
        registry.Register(
            _DeclaredQueueMetadata("orders.issue-invoice", typeof(TestConsumer), "Orders.MessagingModule")
        );

        var act = () =>
            registry.Register(
                _DeclaredQueueMetadata(
                    "billing.issue-invoice",
                    typeof(OtherMessageConsumer),
                    "Billing.MessagingModule"
                ) with
                {
                    MessageName = "billing.close-invoice-command",
                }
            );

        act.Should().NotThrow();
    }

    [Fact]
    public void same_lane_durable_identity_is_independent_across_contract_versions()
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.v1"));

        var act = () => registry.Register(_DurableMetadata(MessageLane.Bus, "orders.v2", contractVersion: "v2"));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("", "v1", "*Consumer identity*")]
    [InlineData(" ", "v1", "*Consumer identity*")]
    [InlineData("orders-projection", "", "*contractVersion*")]
    [InlineData("orders-projection", " ", "*contractVersion*")]
    public void register_rejects_blank_durable_contract_values(
        string consumerIdentity,
        string contractVersion,
        string expectedMessage
    )
    {
        var registry = new ConsumerRegistry();
        var metadata = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "orders.invalid",
            1,
            MessageLane.Bus,
            consumerIdentity,
            contractVersion
        );

        var act = () => registry.Register(metadata);

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData("", "v1", "*Consumer identity*")]
    [InlineData(" ", "v1", "*Consumer identity*")]
    [InlineData("orders-projection", "", "*contractVersion*")]
    [InlineData("orders-projection", " ", "*contractVersion*")]
    public void update_rejects_blank_durable_contract_values(
        string consumerIdentity,
        string contractVersion,
        string expectedMessage
    )
    {
        var registry = new ConsumerRegistry();
        registry.Register(_DurableMetadata(MessageLane.Bus, "orders.original"));
        var metadata = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "orders.invalid",
            1,
            MessageLane.Bus,
            consumerIdentity,
            contractVersion
        );

        var act = () => registry.Update(static _ => true, metadata);

        act.Should().Throw<ArgumentException>().WithMessage(expectedMessage);
    }

    private static ConsumerMetadata _DurableMetadata(
        MessageLane lane,
        string messageName,
        string contractVersion = "v1"
    )
    {
        return new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            messageName,
            1,
            lane,
            ConsumerIdentity: "orders-projection",
            MessageContractVersion: contractVersion
        );
    }

    private static ConsumerMetadata _DeclaredQueueMetadata(string identity, Type consumerType, string module)
    {
        return new ConsumerMetadata(
            typeof(TestMessage),
            consumerType,
            "orders.issue-invoice-command",
            1,
            MessageLane.Queue,
            identity,
            MessageContractVersion: "v1"
        )
        {
            DeclaringModule = module,
            Dispatch = static (_, _, _) => ValueTask.CompletedTask,
        };
    }

    private sealed class OtherMessage;

    private sealed class OtherMessageConsumer : IConsume<OtherMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OtherMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
