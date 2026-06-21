// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Pulsar;

/// <summary>Framework-defined header names used with the Apache Pulsar transport.</summary>
public static class PulsarHeaders
{
    /// <summary>
    /// Header carrying the Pulsar message key, which influences partition routing within a partitioned
    /// Pulsar topic. Messages sharing the same key are routed to the same partition.
    /// </summary>
    public const string PulsarKey = "headless-pulsar-key";
}
