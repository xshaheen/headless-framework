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
            "test.group",
            2,
            IntentType: IntentType.Bus
        );

        // when
        registry.Register(metadata);
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all[0].Should().Be(metadata);
    }

    [Fact]
    public void should_freeze_registry_after_first_getall()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1, IntentType: IntentType.Bus)
        );

        // when
        _ = registry.GetAll(); // Freeze
        var act = () =>
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    "test2",
                    null,
                    1,
                    IntentType: IntentType.Bus
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
            null,
            1,
            IntentType: IntentType.Bus
        );
        var updated = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "updated",
            "group1",
            5,
            IntentType: IntentType.Bus
        );
        registry.Register(original);

        // when
        registry.Update(m => m.ConsumerType == typeof(TestConsumer), updated);
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all[0].Should().Be(updated);
        all[0].MessageName.Should().Be("updated");
        all[0].Group.Should().Be("group1");
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
            null,
            1,
            IntentType: IntentType.Bus
        );
        registry.Register(original);

        // when
        registry.Update(
            m => m.ConsumerType == typeof(OtherConsumer),
            new ConsumerMetadata(typeof(TestMessage), typeof(OtherConsumer), "new", null, 1, IntentType: IntentType.Bus)
        );
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all[0].Should().Be(original);
    }

    [Fact]
    public void should_throw_when_updating_frozen_registry()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1, IntentType: IntentType.Bus)
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
                    null,
                    1,
                    IntentType: IntentType.Bus
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
            new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1, IntentType: IntentType.Bus)
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
                    $"group.{i}",
                    (byte)((i % 10) + 1),
                    IntentType.Bus
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
    public void should_reject_duplicate_topic_and_group_even_when_handler_ids_differ()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "orders.placed",
                "billing",
                1,
                IntentType: IntentType.Bus,
                "Tests.ConsumerA"
            )
        );

        // when
        var act = () =>
            registry.Register(
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(OtherConsumer),
                    "orders.placed",
                    "billing",
                    1,
                    IntentType: IntentType.Bus,
                    "Tests.ConsumerB"
                )
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registration detected for messageName/group identity*");
    }

    [Fact]
    public void should_reject_updates_that_collide_on_topic_and_group()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "orders.placed",
                "billing",
                1,
                IntentType: IntentType.Bus,
                "Tests.ConsumerA"
            )
        );
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(OtherConsumer),
                "orders.cancelled",
                "analytics",
                1,
                IntentType: IntentType.Bus,
                "Tests.ConsumerB"
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
                    "billing",
                    1,
                    IntentType: IntentType.Bus,
                    "Tests.ConsumerB"
                )
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate consumer registration detected for messageName/group identity*");
    }

    [Fact]
    public async Task should_prevent_registration_after_concurrent_freeze()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1, IntentType: IntentType.Bus)
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
                            null,
                            1,
                            IntentType: IntentType.Bus
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
                null,
                1,
                IntentType: IntentType.Bus
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
                    $"group.{i}",
                    (byte)((i % 10) + 1),
                    IntentType.Bus
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

            // Spawn registration tasks
            for (var i = 0; i < registrationsPerIteration; i++)
            {
                var index = i;
                tasks.Add(
                    Task.Run(
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
                                        $"group.{index}",
                                        1,
                                        IntentType: IntentType.Bus
                                    )
                                );
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        },
                        AbortToken
                    )
                );
            }

            // Spawn freeze task
            tasks.Add(
                Task.Run(
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
                    null,
                    1,
                    IntentType: IntentType.Bus
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
                                "group1",
                                5,
                                IntentType: IntentType.Bus
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
            null,
            2,
            IntentType: IntentType.Bus
        );
        registry.Register(metadata);

        // when
        var found = registry.FindByMessageName("test.messageName");

        // then
        found.Should().NotBeNull();
        found.Should().Be(metadata);
    }

    [Fact]
    public void should_find_consumer_by_topic_and_group()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata1 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(TestConsumer),
            "test.messageName",
            "group1",
            2,
            IntentType: IntentType.Bus
        );
        var metadata2 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(OtherConsumer),
            "test.messageName",
            "group2",
            3,
            IntentType: IntentType.Bus
        );
        registry.Register(metadata1);
        registry.Register(metadata2);

        // when
        var found = registry.FindByMessageName("test.messageName", "group2");

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
                null,
                1,
                IntentType: IntentType.Bus
            )
        );

        // when
        var found = registry.FindByMessageName("nonexistent.messageName");

        // then
        found.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_topic_found_but_group_not_found()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(
            new ConsumerMetadata(
                typeof(TestMessage),
                typeof(TestConsumer),
                "test.messageName",
                "group1",
                1,
                IntentType: IntentType.Bus
            )
        );

        // when
        var found = registry.FindByMessageName("test.messageName", "group2");

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
            null,
            1,
            IntentType: IntentType.Bus
        );
        var metadata2 = new ConsumerMetadata(
            typeof(TestMessage),
            typeof(OtherConsumer),
            "topic2",
            null,
            2,
            IntentType: IntentType.Bus
        );
        var metadata3 = new ConsumerMetadata(
            typeof(OtherMessage),
            typeof(OtherMessageConsumer),
            "topic3",
            null,
            3,
            IntentType: IntentType.Bus
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
            null,
            1,
            IntentType: IntentType.Bus
        );
        var metadata2 = new ConsumerMetadata(
            typeof(OtherMessage),
            typeof(OtherMessageConsumer),
            "topic2",
            null,
            2,
            IntentType: IntentType.Bus
        );
        registry.Register(metadata1);
        registry.Register(metadata2);

        // when
        var found = registry.FindByMessageType<OtherMessage>().ToList();

        // then
        found.Should().HaveCount(1);
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
                null,
                1,
                IntentType: IntentType.Bus
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

    private sealed class OtherMessage;

    private sealed class OtherMessageConsumer : IConsume<OtherMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<OtherMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
