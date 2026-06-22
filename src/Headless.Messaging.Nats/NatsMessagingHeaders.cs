// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Nats;

/// <summary>Framework-defined header names used with the NATS JetStream transport.</summary>
[PublicAPI]
public static class NatsMessagingHeaders
{
    /// <summary>
    /// Header carrying the shard token appended to the NATS subject, producing the effective
    /// subject <c>{base}.{shard}</c>. Set via <c>NatsMessageConfigBuilder.SubjectShard</c>.
    /// Consumers of sharded messages must declare <c>.UseNats(c => c.Sharded())</c> to receive
    /// all shard variants; omitting this declaration results in silent message loss.
    /// </summary>
    public const string SubjectShard = "headless-nats-subject-shard";
}
