// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Authors options for ordinary one-shot jobs without resolving inherited policies.</summary>
/// <remarks>
/// Supports sequential reuse; concurrent mutation is not supported. Retry arrays are copied when supplied and
/// on every <see cref="Build"/>. Building does not validate options or accept a job for execution.
/// </remarks>
[PublicAPI]
public sealed class JobOptionsBuilder
{
    private int? _retries;
    private int[]? _retryIntervals;
    private NodeDeathPolicy? _onNodeDeath;
    private bool _requireAtomicEnlistment;
    private string? _correlationId;
    private string? _causationId;
    private string? _description;
    private string? _tenantId;
    private bool _isSystemJob;

    /// <summary>Creates an empty builder that preserves the canonical options defaults.</summary>
    public JobOptionsBuilder() { }

    /// <summary>Sets durable retries; zero disables retries and null restores inheritance.</summary>
    public JobOptionsBuilder WithRetries(int? retries)
    {
        _retries = retries;
        return this;
    }

    /// <summary>Copies retry delays in seconds; null inherits and an empty array replaces inherited intervals.</summary>
    public JobOptionsBuilder WithRetryIntervals(params int[]? retryIntervals)
    {
        _retryIntervals = retryIntervals?.ToArray();
        return this;
    }

    /// <summary>Sets the node-death policy; null restores inheritance.</summary>
    public JobOptionsBuilder WithNodeDeathPolicy(NodeDeathPolicy? policy)
    {
        _onNodeDeath = policy;
        return this;
    }

    /// <summary>Requires compatible relational enlistment; this assertion remains set across builder reuse.</summary>
    public JobOptionsBuilder RequireAtomicEnlistment()
    {
        _requireAtomicEnlistment = true;
        return this;
    }

    /// <summary>Sets business correlation; null restores the scheduler's default correlation behavior.</summary>
    public JobOptionsBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the immediate business cause; null restores the scheduler's default causation behavior.</summary>
    public JobOptionsBuilder WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    /// <summary>Sets the operational description; null removes the explicit description.</summary>
    public JobOptionsBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    /// <summary>Sets the explicit tenant; null defers to configured ambient tenant capture.</summary>
    public JobOptionsBuilder WithTenantId(string? tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Asserts deliberate system scope; this assertion remains set across builder reuse.</summary>
    /// <remarks>The existing scheduling path rejects conflicts with explicit or ambient tenants.</remarks>
    public JobOptionsBuilder AsSystemJob()
    {
        _isSystemJob = true;
        return this;
    }

    /// <summary>Creates an independent options snapshot, leaving this builder available for sequential reuse.</summary>
    /// <returns>The canonical options record with its own retry array, when supplied.</returns>
    public JobOptions Build() =>
        new()
        {
            Retries = _retries,
            RetryIntervals = _retryIntervals?.ToArray(),
            OnNodeDeath = _onNodeDeath,
            RequireAtomicEnlistment = _requireAtomicEnlistment,
            CorrelationId = _correlationId,
            CausationId = _causationId,
            Description = _description,
            TenantId = _tenantId,
            IsSystemJob = _isSystemJob,
        };
}
