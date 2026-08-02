// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.PreviousVersionProbe;

internal static class Program
{
    private const string _ExpectedPackageVersion = "0.11.0";

    public static async Task<int> Main(string[] args)
    {
        if (args is ["verify", var versionProvider])
        {
            await Console.Out.WriteLineAsync($"VERIFIED|{versionProvider}|{_PackageVersion(versionProvider)}");
            return 0;
        }

        if (
            args
            is not [var operation, var provider, var laneText, var endpoint, var logicalName, var group, var messageId]
        )
        {
            await Console.Error.WriteLineAsync(
                "Usage: verify <nats|redis> | <produce|consume> <nats|redis> <bus|queue> <endpoint> <logical-name> <group> <message-id>"
            );
            return 2;
        }

        var lane = _ParseLane(laneText);

        try
        {
            return operation switch
            {
                "produce" => await _ProduceAsync(provider, lane, endpoint, logicalName, messageId),
                "consume" => await _ConsumeAsync(provider, lane, endpoint, logicalName, group, messageId),
                _ => throw new ArgumentException($"Unsupported probe operation '{operation}'.", nameof(args)),
            };
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString());
            return 1;
        }
    }

    private static async Task<int> _ProduceAsync(
        string provider,
        IntentType lane,
        string endpoint,
        string logicalName,
        string messageId
    )
    {
        await using var services = _BuildProvider(provider, endpoint);
        var transport =
            lane == IntentType.Bus
                ? (ITransport)services.GetRequiredService<IBusTransport>()
                : services.GetRequiredService<IQueueTransport>();
        var result = await transport.SendAsync(_CreateMessage(logicalName, lane, messageId));

        if (!result.Succeeded)
        {
            throw result.Exception ?? new InvalidOperationException("The previous-package producer failed.");
        }

        await Console.Out.WriteLineAsync($"PRODUCED|{provider}|{_PackageVersion(provider)}|{messageId}");
        return 0;
    }

    private static async Task<int> _ConsumeAsync(
        string provider,
        IntentType lane,
        string endpoint,
        string logicalName,
        string group,
        string expectedMessageId
    )
    {
        await using var services = _BuildProvider(provider, endpoint);
        var factory =
            services.GetRequiredService<IConsumerClientFactory>() as IIntentAwareConsumerClientFactory
            ?? throw new InvalidOperationException($"The {provider} 0.11.0 package is not intent-aware.");
        await using var consumer = await factory.CreateAsync(group, 1, lane);
        var received = new TaskCompletionSource<(TransportMessage Message, object? Settlement)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        consumer.OnLogCallback = static _ => { };
        consumer.OnMessageCallback = (message, settlement) =>
        {
            received.TrySetResult((message, settlement));
            return Task.CompletedTask;
        };

        var messageNames = await consumer.FetchMessageNamesAsync([logicalName]);
        await consumer.SubscribeAsync(messageNames);
        using var listenCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listening = consumer.ListeningAsync(TimeSpan.FromMilliseconds(100), listenCancellation.Token).AsTask();
        await consumer.WaitUntilReadyAsync(listenCancellation.Token);
        await Console.Out.WriteLineAsync(
            $"READY|{provider}|{_PackageVersion(provider)}|{lane.ToString().ToLowerInvariant()}"
        );

        var delivery = await received.Task.WaitAsync(listenCancellation.Token);
        if (!string.Equals(delivery.Message.Id, expectedMessageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected previous-package delivery '{expectedMessageId}', received '{delivery.Message.Id}'."
            );
        }

        await Console.Out.WriteLineAsync($"RECEIVED|{provider}|{_PackageVersion(provider)}|{delivery.Message.Id}");
        var command = await Console.In.ReadLineAsync(listenCancellation.Token);
        if (string.Equals(command, "COMMIT", StringComparison.Ordinal))
        {
            await consumer.CommitAsync(delivery.Settlement);
            await Console.Out.WriteLineAsync($"DRAINED|{provider}|{_PackageVersion(provider)}|{delivery.Message.Id}");
        }
        else if (string.Equals(command, "ABORT", StringComparison.Ordinal))
        {
            await consumer.RejectAsync(delivery.Settlement);
            await Console.Out.WriteLineAsync($"ABORTED|{provider}|{_PackageVersion(provider)}|{delivery.Message.Id}");
        }
        else
        {
            throw new InvalidOperationException($"Expected COMMIT or ABORT, received '{command ?? "<eof>"}'.");
        }

        await listenCancellation.CancelAsync();
        try
        {
            await listening;
        }
        catch (OperationCanceledException) when (listenCancellation.IsCancellationRequested) { }

        return 0;
    }

    private static ServiceProvider _BuildProvider(string provider, string endpoint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            switch (provider)
            {
                case "nats":
                    setup.UseNats(options =>
                    {
                        options.Servers = endpoint;
                        options.EnableSubscriberClientStreamAndSubjectCreation = false;
                    });
                    break;
                case "redis":
                    setup.UseRedis(endpoint);
                    setup.UseRedisPubSub(endpoint);
                    break;
                default:
                    throw new ArgumentException($"Unsupported probe provider '{provider}'.", nameof(provider));
            }
        });
        return services.BuildServiceProvider();
    }

    private static TransportMessage _CreateMessage(string logicalName, IntentType lane, string messageId) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = messageId,
                [Headers.MessageName] = logicalName,
                [Headers.Intent] = lane.ToString(),
            },
            "previous-package-0.11.0"u8.ToArray()
        );

    private static IntentType _ParseLane(string lane) =>
        lane switch
        {
            "bus" => IntentType.Bus,
            "queue" => IntentType.Queue,
            _ => throw new ArgumentException($"Unsupported probe lane '{lane}'.", nameof(lane)),
        };

    private static string _PackageVersion(string provider)
    {
        var providerAssembly = provider switch
        {
            "nats" => typeof(NatsMessagingOptions).Assembly,
            "redis" => typeof(RedisMessagingOptions).Assembly,
            _ => null,
        };
        var coreAssembly = typeof(ITransport).Assembly;

        if (providerAssembly is null)
        {
            throw new InvalidOperationException("Unable to determine previous-package assembly versions.");
        }

        var providerPackageVersion = _InformationalPackageVersion(providerAssembly);
        var corePackageVersion = _InformationalPackageVersion(coreAssembly);
        if (
            !string.Equals(providerPackageVersion, _ExpectedPackageVersion, StringComparison.Ordinal)
            || !string.Equals(corePackageVersion, _ExpectedPackageVersion, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Expected the complete 0.11.0 probe family, resolved Core {corePackageVersion} and {provider} {providerPackageVersion}."
            );
        }

        return _ExpectedPackageVersion;
    }

    private static string _InformationalPackageVersion(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException($"Assembly {assembly.GetName().Name} has no informational version.");
        }

        var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0 ? informationalVersion : informationalVersion[..metadataSeparator];
    }
}
