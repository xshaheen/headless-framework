// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using BenchmarkDotNet.Attributes;
using Headless.Messaging.Aws;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// The per-send metadata a transport hands the publisher: the relay and a direct publish read
/// <c>BrokerAddress</c> once per message, and the Azure Service Bus bus transport resolves the message's producer
/// descriptor once per send. Neither depends on the message beyond its name, so any allocation here is per-send
/// overhead.
/// </summary>
[MemoryDiagnoser]
public class TransportSendMetadataBenchmarks : IAsyncDisposable
{
    private AzureServiceBusTransport _asbBus = null!;
    private AzureServiceBusQueueTransport _asbQueue = null!;
    private AmazonSnsBusTransport _sns = null!;
    private AmazonSqsQueueTransport _sqs = null!;
    private KafkaProducerProvider _kafkaProvider = null!;
    private KafkaTransport _kafka = null!;
    private TransportMessage _configuredMessage;
    private TransportMessage _defaultMessage;

    [GlobalSetup]
    public void Setup()
    {
        var asbOptions = new AzureServiceBusMessagingOptions
        {
            ConnectionString =
                "Endpoint=sb://bench.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key",
        };

        for (var i = 0; i < 8; i++)
        {
            asbOptions.CustomProducers.Add(
                new ServiceBusProducerDescriptor(
                    string.Create(CultureInfo.InvariantCulture, $"Message{i}"),
                    string.Create(CultureInfo.InvariantCulture, $"topic-{i}")
                )
            );
        }

        var asbAccessor = Options.Create(asbOptions);

        // The client pool is not touched by BrokerAddress or producer lookup.
        _asbBus = new AzureServiceBusTransport(NullLogger<AzureServiceBusTransport>.Instance, asbAccessor, null!);
        _asbQueue = new AzureServiceBusQueueTransport(
            NullLogger<AzureServiceBusQueueTransport>.Instance,
            asbAccessor,
            null!
        );

        var awsAccessor = Options.Create(new AmazonSqsMessagingOptions { Region = RegionEndpoint.EUWest1 });
        _sns = new AmazonSnsBusTransport(NullLogger<AmazonSnsBusTransport>.Instance, awsAccessor);
        _sqs = new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, awsAccessor);

        _kafkaProvider = new KafkaProducerProvider(
            NullLogger<KafkaProducerProvider>.Instance,
            Options.Create(new KafkaMessagingOptions { Servers = "broker-1:9092, broker-2:9092, broker-3:9092" })
        );
        _kafka = new KafkaTransport(NullLogger<KafkaTransport>.Instance, _kafkaProvider);

        _configuredMessage = _Message("Message5");
        _defaultMessage = _Message("Unconfigured");
    }

    [GlobalCleanup]
    public async ValueTask DisposeAsync()
    {
        await _asbBus.DisposeAsync().ConfigureAwait(false);
        await _asbQueue.DisposeAsync().ConfigureAwait(false);
        await _sns.DisposeAsync().ConfigureAwait(false);
        await _sqs.DisposeAsync().ConfigureAwait(false);
        await _kafka.DisposeAsync().ConfigureAwait(false);
        _kafkaProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public BrokerAddress AzureServiceBusBus() => _asbBus.BrokerAddress;

    [Benchmark]
    public BrokerAddress AzureServiceBusQueue() => _asbQueue.BrokerAddress;

    [Benchmark]
    public BrokerAddress Sns() => _sns.BrokerAddress;

    [Benchmark]
    public BrokerAddress Sqs() => _sqs.BrokerAddress;

    [Benchmark]
    public BrokerAddress Kafka() => _kafka.BrokerAddress;

    [Benchmark]
    public IServiceBusProducerDescriptor AzureServiceBusConfiguredProducer() =>
        _asbBus.CreateProducerForMessage(_configuredMessage);

    [Benchmark]
    public IServiceBusProducerDescriptor AzureServiceBusDefaultProducer() =>
        _asbBus.CreateProducerForMessage(_defaultMessage);

    private static TransportMessage _Message(string name) =>
        new(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { Headers.MessageName, name } },
            body: null
        );
}
