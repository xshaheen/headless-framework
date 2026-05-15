using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class MessagingBuilderTest
{
    [Fact]
    public void CanCreateInstanceAndGetService()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IOutboxPublisher, MyProducerService>();
        var builder = new MessagingBuilder(services);
        builder.Should().NotBeNull();

        var count = builder.Services.Count;
        count.Should().Be(1);

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IOutboxPublisher>();
        publisher.Should().NotBeNull();
    }

    [Fact]
    public void CanAddMessagingService()
    {
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(_ => { });
        using var builder = services.BuildServiceProvider();

        var markService = builder.GetService<MessagingMarkerService>();
        markService.Should().NotBeNull();
    }

    [Fact]
    public void CanResolveMessagingOptions()
    {
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(_ => { });
        using var builder = services.BuildServiceProvider();
        var messagingOptions = builder.GetRequiredService<IOptions<MessagingOptions>>().Value;
        messagingOptions.Should().NotBeNull();
    }

    [Fact]
    public async Task CanResolveScheduledPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
        });
        await using var builder = services.BuildServiceProvider();

        var publisher = builder.GetRequiredService<IScheduledPublisher>();
        publisher.Should().NotBeNull();
    }

    private sealed class MyProducerService : IOutboxPublisher
    {
        public Task PublishAsync<T>(
            T? contentObj,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }
    }
}
