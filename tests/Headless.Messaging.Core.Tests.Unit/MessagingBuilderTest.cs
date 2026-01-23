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

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetService<IOutboxPublisher>();
        publisher.Should().NotBeNull();
    }

    [Fact]
    public void CanAddMessagingService()
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
        var messagingOptions = builder.GetRequiredService<IOptions<MessagingOptions>>().Value;
        messagingOptions.Should().NotBeNull();
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

        public Task PublishAsync<T>(
            T? contentObj,
            string? callbackName = null,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        public Task PublishAsync<T>(
            T? contentObj,
            IDictionary<string, string?> headers,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        public void Publish<T>(T? contentObj, string? callbackName = null)
            where T : class
        {
            throw new NotImplementedException();
        }

        public void Publish<T>(T? contentObj, IDictionary<string, string?> headers)
            where T : class
        {
            throw new NotImplementedException();
        }

        public Task PublishDelayAsync<T>(
            TimeSpan delayTime,
            T? contentObj,
            IDictionary<string, string?> headers,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        public Task PublishDelayAsync<T>(
            TimeSpan delayTime,
            T? contentObj,
            string? callbackName = null,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        public void PublishDelay<T>(TimeSpan delayTime, T? contentObj, IDictionary<string, string?> headers)
            where T : class
        {
            throw new NotImplementedException();
        }

        public void PublishDelay<T>(TimeSpan delayTime, T? contentObj, string? callbackName = null)
            where T : class
        {
            throw new NotImplementedException();
        }
    }
}
