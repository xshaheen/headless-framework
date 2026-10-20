// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;

namespace Tests;

/// <summary>
/// Runs the Azure Service Bus provider against the local emulator with <c>AutoProvision = false</c>, on the fixed entities
/// <c>Emulator/Config.json</c> declares. The real namespace stays the evidence for provisioning, the administration API,
/// and every-instance subscriptions, which the emulator cannot stand in for.
/// </summary>
/// <remarks>
/// The emulator and SQL Server start only after the user accepts their licenses by setting
/// <see cref="EulaEnvironmentVariable"/> to <c>Y</c>. Without it every emulator test skips and no container starts.
/// </remarks>
[UsedImplicitly]
public sealed class AzureServiceBusEmulatorFixture : IAsyncLifetime
{
    /// <summary>The manifest profile the emulator leaves report under.</summary>
    public const string ProviderName = "Azure Service Bus Emulator";

    /// <summary>Set to <c>Y</c> to accept the emulator's and SQL Server's license terms and run the emulator tests.</summary>
    public const string EulaEnvironmentVariable = "HEADLESS_ACCEPT_SERVICEBUS_EMULATOR_EULA";

    /// <summary>The topic <c>Config.json</c> declares, with <see cref="FanOutSubscriptions"/> on it.</summary>
    public const string TopicName = "emulator-topic";

    private const string _EulaUrl =
        "https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt";

    // Config.json declares this many queues; each session takes one of its own, so no test reads another's leftovers.
    private const int _QueueCount = 16;

    // Config.json declares one more queue whose max delivery count is this, for the one scenario that spends it.
    private const int _DeliveryLimitQueueMaxDeliveryCount = 3;
    private const string _DeliveryLimitQueueName = "emulator-queue-delivery-limit-3";

    private AzureServiceBusEmulatorContainer? _emulator;
    private int _queuesTaken;
    private int _deliveryLimitQueueTaken;

    /// <summary>The two subscriptions <c>Config.json</c> declares on <see cref="TopicName"/>, each matching every message.</summary>
    public static IReadOnlyList<string> FanOutSubscriptions { get; } = ["emulator-fanout-a", "emulator-fanout-b"];

    public string ConnectionString => _RequireEmulator().ConnectionString;

    public async ValueTask InitializeAsync()
    {
        if (!_IsEulaAccepted)
        {
            return;
        }

        _emulator = await AzureServiceBusEmulatorContainer.StartAsync(amqpHostPort: null, CancellationToken.None);
    }

    /// <summary>Opens a session on a queue no other session of this run has used.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(CancellationToken cancellationToken)
    {
        return CreateQueueSessionAsync(new TransportConformanceSessionOptions(), cancellationToken);
    }

    /// <summary>
    /// Opens a session on a queue no other session of this run has used, with the session's header builder installed.
    /// The emulator cannot create a queue, so a <see cref="TransportConformanceSessionOptions.DeliveryLimit"/> must match
    /// the one <c>Config.json</c> queue declared for it.
    /// </summary>
    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        TransportConformanceSessionOptions options,
        CancellationToken cancellationToken
    )
    {
        AzureServiceBusFixture.RequireSupportedSessionOptions(options);
        var connectionString = ConnectionString;
        var queueName = options.DeliveryLimit switch
        {
            null => _TakeQueue(),
            _DeliveryLimitQueueMaxDeliveryCount => _TakeDeliveryLimitQueue(),
            var limit => throw new NotSupportedException(
                $"Emulator/Config.json declares no queue whose max delivery count is {limit.Value.ToString(CultureInfo.InvariantCulture)}."
            ),
        };
        var group = $"group-{Guid.NewGuid():N}";

        return AzureServiceBusFixture.CreateSessionAsync(
            connectionString,
            MessageLane.Queue,
            queueName,
            group,
            AzureServiceBusMessagingOptions.DefaultTopicPath,
            disposeEntity: null,
            cancellationToken,
            replacementToken =>
                AzureServiceBusFixture.CreateSessionAsync(
                    connectionString,
                    MessageLane.Queue,
                    queueName,
                    group,
                    AzureServiceBusMessagingOptions.DefaultTopicPath,
                    disposeEntity: null,
                    replacementToken,
                    sessionOptions: options
                ),
            sessionOptions: options
        );
    }

    /// <summary>
    /// Takes every message the dead-letter subqueue of the session's queue holds and returns their message ids.
    /// </summary>
    public ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        TransportConsumerConformanceSession session,
        CancellationToken cancellationToken
    )
    {
        return AzureServiceBusFixture.TakeDeadLetteredMessageIdsAsync(
            ConnectionString,
            session.Destination,
            cancellationToken
        );
    }

    /// <summary>Opens a Bus session that reads <see cref="TopicName"/> through <paramref name="subscriptionName"/>.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string subscriptionName,
        string messageName,
        CancellationToken cancellationToken
    )
    {
        return AzureServiceBusFixture.CreateSessionAsync(
            ConnectionString,
            MessageLane.Bus,
            messageName,
            subscriptionName,
            TopicName,
            disposeEntity: null,
            cancellationToken
        );
    }

    /// <summary>
    /// Opens a session on an emulator of its own whose AMQP port is fixed on the host, so restarting it disturbs no other
    /// test and the session finds it again at the address it already holds.
    /// </summary>
    public async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        RequireEulaAccepted();

#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var emulator = await AzureServiceBusEmulatorContainer.StartAsync(BrokerHostPort.Reserve(), cancellationToken);
#pragma warning restore CA2000

        try
        {
            var session = await AzureServiceBusFixture.CreateSessionAsync(
                emulator.ConnectionString,
                MessageLane.Queue,
                _QueueName(1),
                $"group-{Guid.NewGuid():N}",
                AzureServiceBusMessagingOptions.DefaultTopicPath,
                disposeEntity: null,
                cancellationToken
            );

            return new InterruptibleTransportSession(session, emulator.RestartAsync, emulator.DisposeAsync);
        }
        catch
        {
            await emulator.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_emulator is not null)
        {
            await _emulator.DisposeAsync();
        }
    }

    /// <summary>Skips the calling test unless the user opted in to the emulator's and SQL Server's licenses.</summary>
    public static void RequireEulaAccepted()
    {
        if (!_IsEulaAccepted)
        {
            Assert.Skip(
                $"Azure Service Bus emulator tests need {EulaEnvironmentVariable}=Y. Setting it accepts the emulator's "
                    + $"license ({_EulaUrl}) and the SQL Server license of the database the emulator runs on; the tests "
                    + "never accept either on your behalf."
            );
        }
    }

    private static bool _IsEulaAccepted =>
        string.Equals(
            Environment.GetEnvironmentVariable(EulaEnvironmentVariable),
            "Y",
            StringComparison.OrdinalIgnoreCase
        );

    private AzureServiceBusEmulatorContainer _RequireEmulator()
    {
        RequireEulaAccepted();

        return _emulator
            ?? throw new InvalidOperationException("The Azure Service Bus emulator fixture has not been initialized.");
    }

    private string _TakeQueue()
    {
        var index = Interlocked.Increment(ref _queuesTaken);

        return index <= _QueueCount
            ? _QueueName(index)
            : throw new InvalidOperationException(
                $"Every one of the {_QueueCount} emulator queues is in use; declare more in Emulator/Config.json."
            );
    }

    private string _TakeDeliveryLimitQueue()
    {
        return Interlocked.Exchange(ref _deliveryLimitQueueTaken, 1) == 0
            ? _DeliveryLimitQueueName
            : throw new InvalidOperationException(
                $"The emulator queue '{_DeliveryLimitQueueName}' is in use; declare another in Emulator/Config.json."
            );
    }

    private static string _QueueName(int index) =>
        $"emulator-queue-{index.ToString("00", CultureInfo.InvariantCulture)}";
}

[CollectionDefinition("AzureServiceBusEmulator", DisableParallelization = true)]
public sealed class AzureServiceBusEmulatorCollection : ICollectionFixture<AzureServiceBusEmulatorFixture>;
