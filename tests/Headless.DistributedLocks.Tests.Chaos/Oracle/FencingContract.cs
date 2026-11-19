// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Oracle;

/// <summary>How a provider's documented contract treats a grant at or below a token the resource already accepted.</summary>
public enum IssuerRule
{
    /// <summary>Every such grant fails the run: the provider promises tokens that only increase.</summary>
    Hard,

    /// <summary>
    /// Such a grant is only reported when a failover overlapped the span from the grant of the accepted token to the
    /// offending grant, because only a failover may lose the fence counter; any other such grant fails the run.
    /// </summary>
    ExcusedAcrossFailover,
}

/// <summary>
/// What a provider's documented contract promises about the tokens it issues, so the oracle judges each backend by its
/// own guarantee.
/// </summary>
/// <param name="Name">The contract's name in the report.</param>
/// <param name="Issuer">How a grant at or below an accepted token is judged.</param>
public sealed record FencingContract(string Name, IssuerRule Issuer)
{
    /// <summary>
    /// PostgreSQL and SQL Server draw tokens from a durable sequence in the grant statement, and InMemory from one
    /// in-process counter, so a new grant always exceeds every token issued before it
    /// (docs/llms/distributed-locks.md, "Locks and Fencing Tokens" provider table).
    /// </summary>
    public static FencingContract DurableSequence { get; } = new("durable sequence", IssuerRule.Hard);

    /// <summary>
    /// Redis tokens increase while Redis keeps the fence key; only eviction, data loss, or a failover to a replica
    /// that missed a recent acquire may issue a fence value again, because replication is asynchronous and the acquire
    /// script does not wait for replicas (docs/llms/distributed-locks.md, "Locks and Fencing Tokens": the Redis row of
    /// the provider table and the paragraph after it). The scenario configures no eviction and loses no data outside
    /// its failovers, so a reissue the failover does not explain is a defect.
    /// </summary>
    public static FencingContract RedisFailover { get; } =
        new("increasing except across failover", IssuerRule.ExcusedAcrossFailover);
}
