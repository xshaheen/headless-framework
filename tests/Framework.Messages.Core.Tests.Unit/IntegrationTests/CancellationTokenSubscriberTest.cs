using Framework.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Helpers;

namespace Tests.IntegrationTests;

public class CancellationTokenSubscriberTest(ITestOutputHelper testOutput) : IntegrationTestBase(testOutput)
{
    [Fact]
    public async Task Execute()
    {
        await Publisher.PublishAsync(
            nameof(CancellationTokenSubscriberTest),
            "Test Message",
            cancellationToken: AbortToken
        );
        await HandledMessages.WaitOneMessage(CancellationToken);

        // Explicitly stop Bootstrapper to prove the cancellation token works.
        var bootstrapper = Container.GetRequiredService<IBootstrapper>();

        await bootstrapper.DisposeAsync();

        var (message, token) = HandledMessages.OfType<(string Message, CancellationToken Token)>().Single();

        message.Should().Be("Test Message");
        token.IsCancellationRequested.Should().BeTrue();
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<TestEventSubscriber>();
    }

    private sealed class TestEventSubscriber(ILogger<TestEventSubscriber> logger, TestMessageCollector collector)
        : IConsumer
    {
        [CapSubscribe(nameof(CancellationTokenSubscriberTest), Group = TestServiceCollectionExtensions.TestGroupName)]
        public void Handle(string message, CancellationToken cancellationToken)
        {
            logger.LogWarning($"{nameof(Handle)} method called. {message}");
            collector.Add((message, cancellationToken));
        }
    }
}
