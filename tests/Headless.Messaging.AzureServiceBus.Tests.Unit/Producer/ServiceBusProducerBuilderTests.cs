using Headless.Messaging.AzureServiceBus.Producer;

namespace Tests.Producer;

public record MessagePublished;

public sealed class ServiceBusProducerBuilderTests
{
    [Fact]
    public void should_have_properties_correctly_set_when_obsolete_build_method_is_executed()
    {
        var producer = new ServiceBusProducerDescriptorBuilder<MessagePublished>().UseTopic("my-destination").Build();

        producer.Should().NotBeNull();
        producer.TopicPath.Should().Be("my-destination");
        producer.MessageTypeName.Should().Be(nameof(MessagePublished));
    }

    [Theory]
    [InlineData("my-destination1", true)]
    [InlineData("my-destination2", false)]
    public void should_have_properties_correctly_set_when_build_method_is_executed(
        string topicName,
        bool subscriptionEnabled
    )
    {
        var builder = new ServiceBusProducerDescriptorBuilder<MessagePublished>().UseTopic(topicName);

        if (subscriptionEnabled)
        {
            builder.WithSubscription();
        }

        var producer = builder.Build();
        producer.Should().NotBeNull();
        producer.TopicPath.Should().Be(topicName);
        producer.CreateSubscription.Should().Be(subscriptionEnabled);
        producer.MessageTypeName.Should().Be(nameof(MessagePublished));
    }
}
