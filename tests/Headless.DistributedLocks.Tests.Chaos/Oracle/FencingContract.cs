// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Oracle;

/// <summary>
/// What a provider's documented contract promises about the tokens it issues, so the oracle judges each backend by its
/// own guarantee. The accepted-order rule at the protected resource is hard for every provider.
/// </summary>
/// <param name="Name">The contract's name in the report.</param>
/// <param name="IssuerRuleIsHard">
/// Whether a grant at or below a token the resource already accepted fails the run, or is only reported.
/// </param>
public sealed record FencingContract(string Name, bool IssuerRuleIsHard)
{
    /// <summary>
    /// PostgreSQL and SQL Server draw tokens from a durable sequence in the grant statement, and InMemory from one
    /// in-process counter, so a new grant always exceeds every token issued before it
    /// (docs/llms/distributed-locks.md, "Locks and Fencing Tokens" provider table).
    /// </summary>
    public static FencingContract DurableSequence { get; } = new("durable sequence", IssuerRuleIsHard: true);

    /// <summary>
    /// Redis fencing is best-effort across a failover: replication is asynchronous and the acquire script does not
    /// wait for replicas, so a promoted replica that missed a recent acquire can grant the lock again and issue a fence
    /// value already issued (docs/llms/distributed-locks.md, "Locks and Fencing Tokens": the Redis row of the provider
    /// table and the paragraph after it). A reissued or rolled-back token is reported, not failed; the store still
    /// rejects the stale write, which the hard accepted-order rule checks.
    /// </summary>
    public static FencingContract RedisFailover { get; } = new("best-effort across failover", IssuerRuleIsHard: false);
}
