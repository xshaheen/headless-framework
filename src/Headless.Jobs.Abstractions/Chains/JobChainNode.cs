// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>
/// An immutable node in a built <see cref="JobChain"/>. Each node is one step of the chain: it identifies its work
/// either by its <see cref="JobType"/> (a step without arguments) or by a captured <see cref="Payload"/> and its
/// <see cref="PayloadType"/>; both resolve to the generated descriptor at enqueue. It also
/// carries the per-step options and optional execution time, plus the on-success and on-failure continuation edges.
/// </summary>
/// <remarks>
/// Exactly one identity is set: <see cref="JobType"/> is non-<see langword="null"/> for steps without arguments, and
/// <see cref="Payload"/>/<see cref="PayloadType"/> are non-<see langword="null"/> for payload steps. Nodes are
/// produced by <see cref="JobChainBuilder.Build"/> and never mutate afterward.
/// </remarks>
[PublicAPI]
public sealed class JobChainNode
{
    internal JobChainNode(
        Type? jobType,
        object? payload,
        Type? payloadType,
        JobOptions? options,
        DateTimeOffset? executionTime,
        JobChainNode? onSuccess,
        JobChainNode? onFailure
    )
    {
        JobType = jobType;
        Payload = payload;
        PayloadType = payloadType;
        Options = options;
        ExecutionTime = executionTime;
        OnSuccess = onSuccess;
        OnFailure = onFailure;
    }

    /// <summary>
    /// The <c>[Job]</c> class of a step without arguments, or <see langword="null"/> when this node is a payload step.
    /// </summary>
    public Type? JobType { get; }

    /// <summary>
    /// The captured request payload for a payload step, or <see langword="null"/> when this node carries a
    /// <see cref="JobType"/>.
    /// </summary>
    public object? Payload { get; }

    /// <summary>
    /// The compile-time type of <see cref="Payload"/> used to resolve the generated descriptor at enqueue, or
    /// <see langword="null"/> when this node carries an explicit <see cref="JobType"/>.
    /// </summary>
    public Type? PayloadType { get; }

    /// <summary>The per-step options captured verbatim, or <see langword="null"/> when none were supplied.</summary>
    public JobOptions? Options { get; }

    /// <summary>The explicit execution instant for this step, or <see langword="null"/> to run as soon as eligible.</summary>
    public DateTimeOffset? ExecutionTime { get; }

    /// <summary>The child that becomes eligible when this node reaches a success terminal state, or <see langword="null"/>.</summary>
    public JobChainNode? OnSuccess { get; }

    /// <summary>The child that becomes eligible when this node reaches a failure terminal state, or <see langword="null"/>.</summary>
    public JobChainNode? OnFailure { get; }
}
