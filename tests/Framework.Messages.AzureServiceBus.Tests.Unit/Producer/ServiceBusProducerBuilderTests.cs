using Framework.Messages.Producer;
using Xunit;

namespace Tests.Producer;

public record MessagePublished;

public class ServiceBusProducerBuilderTests
{
    [Fact]
    public void Should_HavePropertiesCorrectlySet_When_Obsolete_BuildMethodIsExecuted()
    {
        var producer = new ServiceBusProducerDescriptorBuilder<MessagePublished>().UseTopic("my-destination").Build();

        producer.Should().NotBeNull();
        producer.TopicPath.Should().Be("my-destination");
        producer.MessageTypeName.Should().Be(nameof(MessagePublished));
    }

    [Theory]
    [InlineData("my-destination1", true)]
    [InlineData("my-destination2", false)]
    public void Should_HavePropertiesCorrectlySet_When_BuildMethodIsExecuted(string topicName, bool subscriptionEnabled)
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
