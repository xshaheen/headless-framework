using Framework.Messages;

namespace Tests;

public class ConsumerRegistryTests
{
    [Fact]
    public void should_register_consumer_metadata()
    {
        // given
        var registry = new ConsumerRegistry();
        var metadata = new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test.topic", "test.group", 2);

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
        registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1));

        // when
        _ = registry.GetAll(); // Freeze
        var act = () =>
            registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test2", null, 1));

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
        var original = new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "original", null, 1);
        var updated = new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "updated", "group1", 5);
        registry.Register(original);

        // when
        registry.Update(m => m.ConsumerType == typeof(TestConsumer), updated);
        var all = registry.GetAll();

        // then
        all.Should().ContainSingle();
        all[0].Should().Be(updated);
        all[0].Topic.Should().Be("updated");
        all[0].Group.Should().Be("group1");
        all[0].Concurrency.Should().Be(5);
    }

    [Fact]
    public void should_not_update_if_predicate_matches_nothing()
    {
        // given
        var registry = new ConsumerRegistry();
        var original = new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "original", null, 1);
        registry.Register(original);

        // when
        registry.Update(
            m => m.ConsumerType == typeof(OtherConsumer),
            new ConsumerMetadata(typeof(TestMessage), typeof(OtherConsumer), "new", null, 1)
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
        registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1));
        _ = registry.GetAll(); // Freeze

        // when
        var act = () =>
            registry.Update(
                m => m.ConsumerType == typeof(TestConsumer),
                new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "updated", null, 1)
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
        registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1));

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
                    $"topic.{i}",
                    $"group.{i}",
                    (byte)(i % 10 + 1)
                )
            );
        }

        var all = registry.GetAll();

        // then
        all.Should().HaveCount(consumerCount, "all registrations should succeed");
        all.Select(m => m.Topic)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .HaveCount(consumerCount, "all topics should be unique");
    }

    [Fact]
    public async Task should_prevent_registration_after_concurrent_freeze()
    {
        // given
        var registry = new ConsumerRegistry();
        registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test", null, 1));

        var freezeTask = Task.Run(() => registry.GetAll());
        InvalidOperationException? caughtException = null;

        var registerTask = Task.Run(async () =>
        {
            await Task.Delay(10);
            try
            {
                registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "test2", null, 1));
            }
            catch (InvalidOperationException ex)
            {
                caughtException = ex;
            }
        });

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
        registry.Register(new ConsumerMetadata(typeof(TestMessage), typeof(TestConsumer), "original", null, 1));
        const int updateCount = 50;

        // when
        for (var i = 1; i <= updateCount; i++)
        {
            registry.Update(
                m => m.ConsumerType == typeof(TestConsumer),
                new ConsumerMetadata(
                    typeof(TestMessage),
                    typeof(TestConsumer),
                    $"topic.{i}",
                    $"group.{i}",
                    (byte)(i % 10 + 1)
                )
            );
        }

        var all = registry.GetAll();

        // then
        all.Should().ContainSingle("only one consumer registered");
        all[0].Topic.Should().Be("topic.50", "last update should win");
        all[0].Concurrency.Should().Be(1, "50 % 10 + 1 = 1");
    }

    private sealed class TestMessage;

    private sealed class TestConsumer : IConsume<TestMessage>
    {
        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OtherConsumer : IConsume<TestMessage>
    {
        public ValueTask Consume(ConsumeContext<TestMessage> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
