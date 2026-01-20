using Framework.Messages;
using Framework.Messages.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public class MessagingBuilderTest
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

        var provider = services.BuildServiceProvider();
        var capPublisher = provider.GetService<IOutboxPublisher>();
        capPublisher.Should().NotBeNull();
    }

    [Fact]
    public void CanAddCapService()
    {
        var services = new ServiceCollection();
        services.AddMessages(_ => { });
        var builder = services.BuildServiceProvider();

        var markService = builder.GetService<MessagingMarkerService>();
        markService.Should().NotBeNull();
    }

    [Fact]
    public void CanResolveMessagingOptions()
    {
        var services = new ServiceCollection();
        services.AddMessages(_ => { });
        var builder = services.BuildServiceProvider();
        var capOptions = builder.GetRequiredService<IOptions<MessagingOptions>>().Value;
        capOptions.Should().NotBeNull();
    }

    private sealed class MyProducerService : IOutboxPublisher
    {
        public IServiceProvider ServiceProvider => null!;

        public IOutboxTransaction? Transaction { get; set; }

        public Task PublishAsync<T>(
            string name,
            T? contentObj,
            string? callbackName = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public Task PublishAsync<T>(
            string name,
            T? contentObj,
            IDictionary<string, string?>? optionHeaders,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public void Publish<T>(string name, T? contentObj, string? callbackName = null)
        {
            throw new NotImplementedException();
        }

        public void Publish<T>(string name, T? contentObj, IDictionary<string, string?>? headers)
        {
            throw new NotImplementedException();
        }

        public Task PublishDelayAsync<T>(
            TimeSpan delayTime,
            string name,
            T? value,
            IDictionary<string, string?> headers,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public Task PublishDelayAsync<T>(
            TimeSpan delayTime,
            string name,
            T? value,
            string? callbackName = null,
            CancellationToken cancellationToken = default
        )
        {
            throw new NotImplementedException();
        }

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, IDictionary<string, string?> headers)
        {
            throw new NotImplementedException();
        }

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, string? callbackName = null)
        {
            throw new NotImplementedException();
        }
    }
}
