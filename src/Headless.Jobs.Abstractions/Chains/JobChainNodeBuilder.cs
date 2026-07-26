// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// A mutable handle to a single node while a <see cref="JobChain"/> is being authored. Use <see cref="Then{TRequest}"/>
/// / <see cref="Catch{TRequest}"/> to attach a single on-success / on-failure continuation; each returns the new child
/// handle so the branch can be extended further. Once <see cref="JobChainBuilder.Build"/> has been called the owning
/// chain is frozen and every authoring method on its handles throws.
/// </summary>
[PublicAPI]
public sealed class JobChainNodeBuilder
{
    private readonly JobChainBuilder _owner;

    internal JobChainNodeBuilder(
        JobChainBuilder owner,
        JobFunctionDescriptor? descriptor,
        object? payload,
        Type? payloadType,
        EnqueueOptions? options,
        DateTime? executionTime
    )
    {
        _owner = owner;
        Descriptor = descriptor;
        Payload = payload;
        PayloadType = payloadType;
        Options = options;
        ExecutionTime = executionTime;
    }

    /// <summary>
    /// Attaches the on-success continuation for this node using a request payload whose descriptor is resolved at
    /// enqueue.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type used to resolve the generated descriptor.</typeparam>
    /// <param name="payload">The request payload to run when this node succeeds.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the child step.</param>
    /// <returns>The new child handle so the success branch can be extended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A success edge already exists on this node, or the owning chain has already been built.
    /// </exception>
    public JobChainNodeBuilder Then<TRequest>(
        TRequest payload,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);

        return _AddSuccess(descriptor: null, payload, typeof(TRequest), options, executionTime);
    }

    /// <summary>Attaches the on-success continuation for this node using an explicit generated descriptor.</summary>
    /// <param name="descriptor">The generated descriptor of the requestless step to run when this node succeeds.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the child step.</param>
    /// <returns>The new child handle so the success branch can be extended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A success edge already exists on this node, or the owning chain has already been built.
    /// </exception>
    public JobChainNodeBuilder Then(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return _AddSuccess(descriptor, payload: null, payloadType: null, options, executionTime);
    }

    /// <summary>
    /// Attaches the on-failure continuation for this node using a request payload whose descriptor is resolved at
    /// enqueue. <c>Catch</c> is pure on-failure sugar: it never consumes or rewrites this node's failure.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type used to resolve the generated descriptor.</typeparam>
    /// <param name="payload">The request payload to run when this node fails.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the child step.</param>
    /// <returns>The new child handle so the failure branch can be extended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A failure edge already exists on this node, or the owning chain has already been built.
    /// </exception>
    public JobChainNodeBuilder Catch<TRequest>(
        TRequest payload,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);

        return _AddFailure(descriptor: null, payload, typeof(TRequest), options, executionTime);
    }

    /// <summary>Attaches the on-failure continuation for this node using an explicit generated descriptor.</summary>
    /// <param name="descriptor">The generated descriptor of the requestless step to run when this node fails.</param>
    /// <param name="options">Optional per-step options.</param>
    /// <param name="executionTime">Optional explicit UTC execution time for the child step.</param>
    /// <returns>The new child handle so the failure branch can be extended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A failure edge already exists on this node, or the owning chain has already been built.
    /// </exception>
    public JobChainNodeBuilder Catch(
        JobFunctionDescriptor descriptor,
        EnqueueOptions? options = null,
        DateTime? executionTime = null
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return _AddFailure(descriptor, payload: null, payloadType: null, options, executionTime);
    }

    internal JobFunctionDescriptor? Descriptor { get; }

    internal object? Payload { get; }

    internal Type? PayloadType { get; }

    internal EnqueueOptions? Options { get; }

    internal DateTime? ExecutionTime { get; }

    internal JobChainNodeBuilder? OnSuccessNode { get; private set; }

    internal JobChainNodeBuilder? OnFailureNode { get; private set; }

    private JobChainNodeBuilder _AddSuccess(
        JobFunctionDescriptor? descriptor,
        object? payload,
        Type? payloadType,
        EnqueueOptions? options,
        DateTime? executionTime
    )
    {
        _owner.EnsureNotBuilt();

        if (OnSuccessNode is not null)
        {
            throw new InvalidOperationException(
                "A success edge (Then) is already defined for this chain node; each node allows at most one on-success child."
            );
        }

        var child = new JobChainNodeBuilder(_owner, descriptor, payload, payloadType, options, executionTime);
        OnSuccessNode = child;

        return child;
    }

    private JobChainNodeBuilder _AddFailure(
        JobFunctionDescriptor? descriptor,
        object? payload,
        Type? payloadType,
        EnqueueOptions? options,
        DateTime? executionTime
    )
    {
        _owner.EnsureNotBuilt();

        if (OnFailureNode is not null)
        {
            throw new InvalidOperationException(
                "A failure edge (Catch) is already defined for this chain node; each node allows at most one on-failure child."
            );
        }

        var child = new JobChainNodeBuilder(_owner, descriptor, payload, payloadType, options, executionTime);
        OnFailureNode = child;

        return child;
    }
}
