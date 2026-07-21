// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// An immutable node in a built <see cref="JobChain"/>. Each node is one step of the chain: it identifies its work
/// either by an explicit generated <see cref="JobFunctionDescriptor"/> (a requestless step) or by a captured
/// <see cref="Payload"/> and its <see cref="PayloadType"/> (resolved to a descriptor later, at enqueue). It also
/// carries the per-step options and optional execution time, plus the on-success and on-failure continuation edges.
/// </summary>
/// <remarks>
/// Exactly one identity is set: <see cref="Descriptor"/> is non-<see langword="null"/> for requestless steps, and
/// <see cref="Payload"/>/<see cref="PayloadType"/> are non-<see langword="null"/> for payload steps. Nodes are
/// produced by <see cref="JobChainBuilder.Build"/> and never mutate afterward.
/// </remarks>
[PublicAPI]
public sealed class JobChainNode
{
    internal JobChainNode(
        JobFunctionDescriptor? descriptor,
        object? payload,
        Type? payloadType,
        EnqueueOptions? options,
        DateTime? executionTime,
        JobChainNode? onSuccess,
        JobChainNode? onFailure
    )
    {
        Descriptor = descriptor;
        Payload = payload;
        PayloadType = payloadType;
        Options = options;
        ExecutionTime = executionTime;
        OnSuccess = onSuccess;
        OnFailure = onFailure;
    }

    /// <summary>
    /// The explicit generated descriptor for a requestless step, or <see langword="null"/> when this node is a payload
    /// step whose descriptor is resolved at enqueue.
    /// </summary>
    public JobFunctionDescriptor? Descriptor { get; }

    /// <summary>
    /// The captured request payload for a payload step, or <see langword="null"/> when this node carries an explicit
    /// <see cref="Descriptor"/>.
    /// </summary>
    public object? Payload { get; }

    /// <summary>
    /// The compile-time type of <see cref="Payload"/> used to resolve the generated descriptor at enqueue, or
    /// <see langword="null"/> when this node carries an explicit <see cref="Descriptor"/>.
    /// </summary>
    public Type? PayloadType { get; }

    /// <summary>The per-step options captured verbatim, or <see langword="null"/> when none were supplied.</summary>
    public EnqueueOptions? Options { get; }

    /// <summary>The explicit UTC execution time for this step, or <see langword="null"/> to run as soon as eligible.</summary>
    public DateTime? ExecutionTime { get; }

    /// <summary>The child that becomes eligible when this node reaches a success terminal state, or <see langword="null"/>.</summary>
    public JobChainNode? OnSuccess { get; }

    /// <summary>The child that becomes eligible when this node reaches a failure terminal state, or <see langword="null"/>.</summary>
    public JobChainNode? OnFailure { get; }
}
