// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Models;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// The mutable authoring surface for a <see cref="JobChain"/>, returned by the <see cref="JobChain.Start{TRequest}"/>
/// factories. Extend the tree through <see cref="Root"/> (and the child handles it returns), then call
/// <see cref="Build"/> once to freeze it into an immutable <see cref="JobChain"/>. After <see cref="Build"/> the
/// builder and all of its node handles reject further authoring.
/// </summary>
[PublicAPI]
public sealed class JobChainBuilder
{
    internal JobChainBuilder(
        JobFunctionDescriptor? descriptor,
        object? payload,
        Type? payloadType,
        EnqueueOptions? options,
        DateTime? executionTime
    )
    {
        Root = new JobChainNodeBuilder(this, descriptor, payload, payloadType, options, executionTime);
    }

    /// <summary>The handle to the root step; attach continuations to it with <c>Then</c> / <c>Catch</c>.</summary>
    public JobChainNodeBuilder Root { get; }

    internal bool IsBuilt { get; private set; }

    /// <summary>
    /// Validates the authored tree and freezes it into an immutable <see cref="JobChain"/>. Each builder produces a
    /// single chain: after the first successful call the builder and its handles are immutable.
    /// </summary>
    /// <returns>The immutable chain consumed by the scheduler's chain enqueue path.</returns>
    /// <exception cref="InvalidOperationException">
    /// The builder has already been built, or the longest root-to-leaf path (counting on-success and on-failure edges
    /// alike) exceeds <see cref="JobChain.MaxStructuralDepth"/>.
    /// </exception>
    public JobChain Build()
    {
        if (IsBuilt)
        {
            throw new InvalidOperationException(
                "This chain builder has already been built; each builder produces a single immutable JobChain."
            );
        }

        var depth = _MaxDepth(Root);

        if (depth > JobChain.MaxStructuralDepth)
        {
            throw new InvalidOperationException(
                $"The chain has a depth of {depth} nodes, which exceeds the maximum structural depth of "
                    + $"{JobChain.MaxStructuralDepth} nodes (on-success and on-failure edges both count toward depth)."
            );
        }

        IsBuilt = true;

        return new JobChain(_Freeze(Root), depth);
    }

    internal void EnsureNotBuilt()
    {
        if (IsBuilt)
        {
            throw new InvalidOperationException(
                "The chain has already been built and is now immutable; author the whole chain before calling Build()."
            );
        }
    }

    private static int _MaxDepth(JobChainNodeBuilder node)
    {
        var success = node.OnSuccessNode is null ? 0 : _MaxDepth(node.OnSuccessNode);
        var failure = node.OnFailureNode is null ? 0 : _MaxDepth(node.OnFailureNode);

        return 1 + Math.Max(success, failure);
    }

    private static JobChainNode _Freeze(JobChainNodeBuilder node)
    {
        var onSuccess = node.OnSuccessNode is null ? null : _Freeze(node.OnSuccessNode);
        var onFailure = node.OnFailureNode is null ? null : _Freeze(node.OnFailureNode);

        return new JobChainNode(
            node.Descriptor,
            node.Payload,
            node.PayloadType,
            node.Options,
            node.ExecutionTime,
            onSuccess,
            onFailure
        );
    }
}
