// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// An immutable, typed authoring model for a conditional sequential job chain. A chain is a tree of steps where each
/// step attaches at most one on-success child (<c>Then</c>) and at most one on-failure child (<c>Catch</c>); the
/// scheduler resolves every step's generated descriptor and persists the whole tree atomically at enqueue.
/// </summary>
/// <remarks>
/// Author a chain with the <see cref="Start{TRequest}(TRequest, EnqueueOptions?, DateTime?)"/> factories, extend it
/// through the returned <see cref="JobChainBuilder"/>, then call <see cref="JobChainBuilder.Build"/> to obtain an
/// immutable instance. The chain never references a handler contract (no <c>TJob</c>, <c>IJob&lt;TArgs&gt;</c>, or
/// <c>ICronJob</c>): step identity is a generated <see cref="JobFunctionDescriptor"/>, resolved from the captured
/// payload type where a step supplies a payload.
/// </remarks>
[PublicAPI]
public sealed class JobChain
{
    /// <summary>
    /// The maximum number of nodes allowed on any single root-to-leaf path (on-success and on-failure edges both
    /// count). Enforced by <see cref="JobChainBuilder.Build"/> as a hard structural guard; it is well above the default
    /// configured chain-depth limit and doubles as the ceiling that limit may be configured to.
    /// </summary>
    public const int MaxStructuralDepth = 64;

    internal JobChain(JobChainNode root)
    {
        Root = root;
    }

    /// <summary>The immutable root step of the chain.</summary>
    public JobChainNode Root { get; }

    /// <summary>Starts a chain whose root is a payload step; its descriptor is resolved at enqueue from the payload type.</summary>
    /// <typeparam name="TRequest">The request payload type used to resolve the generated descriptor.</typeparam>
    /// <param name="payload">The request payload for the root step.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the root step.</param>
    /// <returns>A mutable builder positioned at the root; extend it and call <see cref="JobChainBuilder.Build"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    public static JobChainBuilder Start<TRequest>(
        TRequest payload,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new JobChainBuilder(descriptor: null, payload, payloadType: typeof(TRequest), options, executionTime);
    }

    /// <summary>Starts a chain whose root is a requestless step identified by an explicit generated descriptor.</summary>
    /// <param name="descriptor">The generated descriptor of the requestless root step.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the root step.</param>
    /// <returns>A mutable builder positioned at the root; extend it and call <see cref="JobChainBuilder.Build"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    public static JobChainBuilder Start(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new JobChainBuilder(descriptor, payload: null, payloadType: null, options, executionTime);
    }
}
