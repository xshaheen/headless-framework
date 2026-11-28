// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal static class RedisStreamManagerExtensions
{
    /// <summary>
    /// Creates <paramref name="consumerGroup"/> on <paramref name="stream"/>, and the stream with it, in one
    /// <c>XGROUP CREATE ... MKSTREAM</c>. A group that already exists is left as it is, so the call is idempotent.
    /// </summary>
    public static async Task EnsureConsumerGroupAsync(
        this IDatabase database,
        RedisKey stream,
        RedisValue consumerGroup
    )
    {
        try
        {
            await database
                .StreamCreateConsumerGroupAsync(stream, consumerGroup, StreamPosition.NewMessages, createStream: true)
                .ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.GetRedisErrorType() == RedisErrorTypes.GroupAlreadyExists)
        {
            // BUSYGROUP: another process, or an earlier run, created it first.
        }
    }

    /// <summary>Whether the server answered <c>NOGROUP</c>: the stream or its consumer group does not exist.</summary>
    public static bool IsNoGroup(this RedisServerException exception)
    {
        return exception.Message.StartsWith("NOGROUP", StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure a missing stream or consumer group raises: the running host never creates it, so the message
    /// names the deploy step that does.
    /// </summary>
    public static ResourceProvisioningException MissingConsumerGroup(
        string consumerGroup,
        string streams,
        Exception? innerException = null
    )
    {
        return new ResourceProvisioningException(
            $"Redis Streams consumer group '{consumerGroup}' on stream '{streams}' does not exist. Run 'resources provision' (dotnet <app>.dll resources provision) in the deployment, or call AddDeployStepsOnStartup() in development.",
            innerException
        );
    }
}
